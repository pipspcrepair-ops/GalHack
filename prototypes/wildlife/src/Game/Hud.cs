using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace Wildlife;

/// <summary>
/// A deliberately minimal debug HUD: status text, the hold's contents, a crosshair, a red
/// flash when something lands a hit, a scan readout while you hold Q on a creature, and a
/// brief discovery card the first time anyone meets a species. The real diegetic UI is another
/// prototype's job.
///
/// The discovery card never gets in the way. Nothing on this HUD takes the mouse or keyboard
/// focus, so clicks and keys always reach the game. The card waits while a creature is winding
/// up or lunging at the player, or the player is down, and shows a moment after the danger has
/// passed. It slides in at the top, holds for a few seconds and fades.
/// </summary>
public partial class Hud : CanvasLayer
{
    /// <summary>How long a card takes to come in, stays, and takes to go, in seconds.</summary>
    public const float CardIn = 0.25f, CardHold = 3.6f, CardOut = 0.5f;
    /// <summary>A card held back by combat waits this long after the last attack before it shows.</summary>
    public const float CalmBeforeCard = 1.0f;

    Label _text, _hold, _hint, _scan;
    ColorRect _hurt;
    float _hurtTime;

    PanelContainer _card;
    Label _cardKicker, _cardName, _cardWhat, _cardWhere;
    StyleBoxFlat _cardStyle;
    readonly Queue<CodexEntry> _pending = new();
    CodexEntry _showing;
    float _cardAge = -1f, _calm;

    /// <summary>Screenshots turn the cards off, except the one staged to show a card.</summary>
    public bool ShowCards = true;

    /// <summary>True while the card must wait: something is attacking the player, or the player is down.</summary>
    public Func<bool> Danger = () => false;

    /// <summary>Raised when a card actually appears on screen (the game plays the discovery chime).</summary>
    public event Action<CodexEntry> CardShown;

    /// <summary>The species currently on the card, or null.</summary>
    public CodexEntry ShowingCard => _cardAge >= 0f ? _showing : null;
    public int PendingCards => _pending.Count;
    public bool CardVisible => _card.Visible;
    public Control Card => _card;
    /// <summary>Everything the card says, line by line.</summary>
    public string CardText => $"{_cardKicker.Text}\n{_cardName.Text}\n{_cardWhat.Text}\n{_cardWhere.Text}";

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
            MouseFilter = Control.MouseFilterEnum.Ignore,
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

        BuildCard();
        // Nothing on the HUD may ever take a click or the keyboard: play always comes first.
        foreach (Node n in FindChildren("*", "Control", true, false))
            if (n is Control c)
            {
                c.MouseFilter = Control.MouseFilterEnum.Ignore;
                c.FocusMode = Control.FocusModeEnum.None;
            }
    }

    Label MakeLabel(Vector2 at, int size, Color color, Node parent = null)
    {
        var l = new Label { Position = at, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        l.AddThemeConstantOverride("outline_size", 6);
        (parent ?? this).AddChild(l);
        return l;
    }

    /// <summary>
    /// The card: a slim dark slip at the top of the screen, a stripe in the species' own colour
    /// down its left edge. Up out of the way of the crosshair, the scan readout and the hold.
    /// </summary>
    void BuildCard()
    {
        _card = new PanelContainer { Name = "DiscoveryCard", Visible = false };
        _card.AnchorLeft = _card.AnchorRight = 0.5f;
        _card.OffsetLeft = -230f;
        _card.OffsetRight = 230f;
        _card.OffsetTop = 18f;
        _cardStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.07f, 0.09f, 0.82f),
            BorderColor = new Color("7cf9ff"),
            BorderWidthLeft = 6,
            ContentMarginLeft = 18, ContentMarginRight = 16, ContentMarginTop = 8, ContentMarginBottom = 10,
            CornerRadiusTopLeft = 3, CornerRadiusBottomLeft = 3, CornerRadiusTopRight = 6, CornerRadiusBottomRight = 6,
        };
        _card.AddThemeStyleboxOverride("panel", _cardStyle);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 1);
        _card.AddChild(box);
        _cardKicker = MakeLabel(Vector2.Zero, 12, new Color("8fd8d0"), box);
        _cardName = MakeLabel(Vector2.Zero, 25, new Color("f4f1ea"), box);
        _cardWhat = MakeLabel(Vector2.Zero, 15, new Color("d8e2e0"), box);
        _cardWhere = MakeLabel(Vector2.Zero, 13, new Color("93a3a6"), box);
        foreach (Label l in new[] { _cardKicker, _cardName, _cardWhat, _cardWhere }) l.AddThemeConstantOverride("outline_size", 0);
        AddChild(_card);
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

    // ---- The discovery card -------------------------------------------------------------

    /// <summary>A species was met for the first time: its card shows as soon as it safely can.</summary>
    public void QueueDiscovery(CodexEntry entry, Color stripe)
    {
        if (!ShowCards) return;
        _pending.Enqueue(entry);
        _stripes[entry.SpeciesId] = stripe;
    }

    readonly Dictionary<string, Color> _stripes = new();

    /// <summary>Drops every card, shown or waiting (switching worlds, staging screenshots).</summary>
    public void ClearCards()
    {
        _pending.Clear();
        _showing = null;
        _cardAge = -1f;
        _card.Visible = false;
    }

    void ShowCard(CodexEntry e)
    {
        _showing = e;
        _cardAge = 0f;
        _resume = 0f;
        CardOnScreen = 0f;
        _cardKicker.Text = "NEW SPECIES";
        _cardName.Text = e.Name;
        _cardWhat.Text = $"{Capitalise(e.CommonName)}  ·  {e.Form}, {e.Behaviour}  ·  {e.Size:F1} m";
        _cardWhere.Text = $"{e.Planet}  ·  first found by {e.Finder}";
        if (_stripes.TryGetValue(e.SpeciesId, out Color stripe)) _cardStyle.BorderColor = stripe;
        CardShown?.Invoke(e);
    }

    float _resume;

    /// <summary>How long the current (or last) card has been on screen, in the HUD's own time.</summary>
    public float CardOnScreen { get; private set; }

    /// <summary>
    /// One card at a time, each shown once. If something starts attacking the player while a
    /// card is up, the card vanishes at once and its clock stops; once things have been calm
    /// for a moment it slides back in for the rest of its time.
    /// </summary>
    void UpdateCard(float dt)
    {
        bool danger = Danger();
        _calm = danger ? 0f : _calm + dt;
        bool clear = !danger && _calm >= CalmBeforeCard;

        if (_cardAge < 0f && _pending.Count > 0 && clear) ShowCard(_pending.Dequeue());
        if (_cardAge < 0f) return;

        if (!clear)
        {
            _resume = 0f;
            _card.Visible = false;
            return;
        }
        _cardAge += dt;
        _resume += dt;
        CardOnScreen += dt;
        float total = CardIn + CardHold + CardOut;
        if (_cardAge >= total)
        {
            _cardAge = -1f;
            _showing = null;
            _card.Visible = false;
            return;
        }
        float life = _cardAge < CardIn ? _cardAge / CardIn : Math.Clamp((total - _cardAge) / CardOut, 0f, 1f);
        float enter = Ease(Math.Clamp(_resume / CardIn, 0f, 1f));
        _card.Modulate = new Color(1f, 1f, 1f, MathF.Min(life, enter));
        _card.OffsetTop = 18f - (1f - enter) * 14f;
        _card.OffsetBottom = _card.OffsetTop;
        _card.Visible = true;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _hurtTime = MathF.Max(0f, _hurtTime - dt * 1.8f);
        _hurt.Color = new Color(0.8f, 0.05f, 0.05f, _hurtTime * 0.35f);
        UpdateCard(dt);
    }

    static float Ease(float t) => 1f - (1f - t) * (1f - t);

    static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
