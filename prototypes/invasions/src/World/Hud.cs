using System.Collections.Generic;
using System.Linq;
using Godot;
using Invasions.Combat;
using Invasions.Director;

namespace Invasions.World;

/// <summary>
/// The player's visor. Kept simple and readable: a status panel (base, invasion, siren, invaders,
/// storage), the latest warning as a banner, a big arrival countdown, a red pulse while sirens sound,
/// a long-range scan, health and the current prompt.
/// </summary>
public partial class Hud : CanvasLayer
{
    public InvasionController Controller;
    public Player Player;

    RichTextLabel _status;
    Label _banner, _countdown, _prompt, _help, _badge;
    PanelContainer _bannerPanel, _badgePanel;
    TextureRect _vignette;
    ColorRect _hurt, _healthFill;
    ScanDisplay _scan;
    float _bannerTime, _hurtFlash;
    int _bannerLevel;

    static readonly Color Ink = new("ffe8c2");
    const string Amber = "#ffb703", Red = "#ff4d3d", Green = "#9ef01a", Teal = "#4cc9f0", Dim = "#c9b99a";

    public override void _Ready()
    {
        Layer = 1;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // A red edge glow that pulses with the sirens.
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1, 0, 0, 0));
        gradient.SetColor(1, new Color(1f, 0.08f, 0.04f, 0.36f));
        gradient.SetOffset(0, 0.62f);
        _vignette = new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = gradient, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1.05f, 0.5f), Width = 256, Height = 256 },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        _vignette.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_vignette);

        _hurt = new ColorRect { Color = new Color(0.8f, 0.05f, 0.02f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _hurt.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_hurt);

        // Status panel, bottom left, leaving the top of the visor for warnings.
        var panel = Panel(new Color(0.06f, 0.07f, 0.09f, 0.72f), Palette.Amber);
        Pin(panel, 0, 1, 16, -100, 616, -100);
        panel.GrowVertical = Control.GrowDirection.Begin;
        root.AddChild(panel);
        _status = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            CustomMinimumSize = new Vector2(600, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _status.AddThemeFontOverride("normal_font", Palette.Font);
        _status.AddThemeFontOverride("bold_font", Palette.Font);
        _status.AddThemeFontSizeOverride("normal_font_size", 19);
        _status.AddThemeFontSizeOverride("bold_font_size", 19);
        _status.AddThemeColorOverride("default_color", Ink);
        _status.AddThemeConstantOverride("line_separation", 3);
        panel.AddChild(_status);

        // Warning banner and countdown, top centre: what the design calls the visor warning.
        _bannerPanel = Panel(new Color(0.08f, 0.03f, 0.02f, 0.82f), Palette.SirenRed);
        Pin(_bannerPanel, 0.5f, 0, -340, 14, 340, 14);
        _bannerPanel.GrowVertical = Control.GrowDirection.End;
        root.AddChild(_bannerPanel);
        _banner = MakeLabel(22, Ink);
        _banner.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.CustomMinimumSize = new Vector2(640, 0);
        _bannerPanel.AddChild(_banner);
        _bannerPanel.Visible = false;

        _countdown = MakeLabel(40, Palette.Amber);
        Pin(_countdown, 0.5f, 0, -300, 124, 300, 174);
        _countdown.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(_countdown);

        _badge = MakeLabel(18, new Color("0c1418"));
        _badgePanel = Panel(Palette.Amber, Palette.Amber);
        Pin(_badgePanel, 1, 0, -200, 16, -16, 16);
        _badgePanel.GrowHorizontal = Control.GrowDirection.Begin;
        _badgePanel.AddChild(_badge);
        root.AddChild(_badgePanel);

        // Crosshair.
        foreach (Vector2 size in new[] { new Vector2(14, 2), new Vector2(2, 14) })
        {
            var bar = new ColorRect { Color = new Color(1, 1, 1, 0.75f), MouseFilter = Control.MouseFilterEnum.Ignore };
            Pin(bar, 0.5f, 0.5f, -size.X / 2, -size.Y / 2, size.X / 2, size.Y / 2);
            root.AddChild(bar);
        }

        _prompt = MakeLabel(21, Palette.Yellow);
        Pin(_prompt, 0.5f, 0.5f, -450, 56, 450, 90);
        _prompt.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(_prompt);

        _help = MakeLabel(15, new Color(1f, 0.91f, 0.76f, 0.85f));
        Pin(_help, 0, 1, 16, -52, 1000, -8);
        _help.Text = "WASD move · Shift sprint · Space jump · Ctrl crouch · LMB fire · F man turret · E recover crate\n" +
                     "I invade now (again: skip to next warning) · T fast time · P place new core · K destroy garrison core · Esc mouse";
        root.AddChild(_help);

        // Health bar, bottom left above the help.
        var healthBack = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = Control.MouseFilterEnum.Ignore };
        Pin(healthBack, 0, 1, 16, -80, 276, -66);
        root.AddChild(healthBack);
        _healthFill = new ColorRect { Color = Palette.Teal, Size = new Vector2(260, 14), MouseFilter = Control.MouseFilterEnum.Ignore };
        healthBack.AddChild(_healthFill);

        _scan = new ScanDisplay { Controller = Controller, Player = Player };
        Pin(_scan, 1, 1, -276, -300, -16, -20);
        root.AddChild(_scan);

        Player.Hurt += taken => _hurtFlash = Mathf.Min(0.45f, 0.12f + taken / 60f);
    }

    /// <summary>Pins a control by anchors and pixel offsets (left, top, right, bottom).</summary>
    static T Pin<T>(T c, float anchorX, float anchorY, float left, float top, float right, float bottom) where T : Control
    {
        c.AnchorLeft = c.AnchorRight = anchorX;
        c.AnchorTop = c.AnchorBottom = anchorY;
        c.OffsetLeft = left;
        c.OffsetTop = top;
        c.OffsetRight = right;
        c.OffsetBottom = bottom;
        return c;
    }

    static PanelContainer Panel(Color fill, Color edge)
    {
        var style = new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = edge,
            BorderWidthLeft = 4,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    static Label MakeLabel(int size, Color color)
    {
        var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", Palette.Font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("outline_size", 5);
        return label;
    }

    /// <summary>Puts a message on the visor banner. Level 1-3 for warnings, 0 for other news.</summary>
    public void Announce(string text, int level, float seconds = 9f)
    {
        _banner.Text = level > 0 ? $"WARNING {level} OF 3   ·   {text}" : text;
        _bannerLevel = level;
        _bannerTime = seconds;
        _bannerPanel.Visible = true;
        var style = (StyleBoxFlat)_bannerPanel.GetThemeStylebox("panel");
        style.BorderColor = level >= 3 ? Palette.SirenRed : level > 0 ? Palette.Amber : Palette.Teal;
    }

    public string BannerText => _bannerPanel.Visible ? _banner.Text : "";
    public string StatusText => _status.GetParsedText();

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        InvasionController c = Controller;
        InvasionDirector d = c.Director;
        Invasion inv = c.Current;

        _bannerTime -= dt;
        if (_bannerTime <= 0f && _bannerPanel.Visible && !(inv is { Phase: InvasionPhase.Incoming } && _bannerLevel > 0)) _bannerPanel.Visible = false;

        float pulse = c.View.Sirens.Pulse;
        _vignette.Modulate = new Color(1, 1, 1, c.View.Sirens.On ? 0.15f + 0.45f * pulse : 0f);
        _hurtFlash = Mathf.MoveToward(_hurtFlash, 0f, dt * 1.2f);
        _hurt.Color = _hurt.Color with { A = _hurtFlash };

        _countdown.Visible = inv is { Phase: InvasionPhase.Incoming };
        if (inv is { Phase: InvasionPhase.Incoming })
        {
            _countdown.Text = $"ARRIVAL  T-{Clock(inv.SecondsToArrival(d.Now))}";
            _countdown.AddThemeColorOverride("font_color", inv.WarningLevel >= 3 ? Palette.SirenRed : Palette.Amber);
        }

        _badgePanel.Visible = d.FastTime;
        _badge.Text = $"FAST TIME x{d.Server.FastTimeScale:0}";

        _status.Text = StatusBbcode(c, d, inv);
        _prompt.Text = Player.Prompt;
        _healthFill.Size = new Vector2(260f * (float)Player.Health.Fraction, 14);
        _healthFill.Color = Player.Health.Fraction < 0.25 ? Palette.SirenRed : Palette.Teal;
        _scan.QueueRedraw();
    }

    string StatusBbcode(InvasionController c, InvasionDirector d, Invasion inv)
    {
        var model = c.Model;
        string baseState = !model.Online
            ? $"[color={Red}]OFFLINE · core destroyed · no power, turrets off[/color]  [color={Dim}](P: place new core)[/color]"
            : !model.Powered
                ? $"[color={Amber}]ONLINE · generator down, no power[/color]"
                : $"[color={Green}]ONLINE · POWERED[/color]  [color={Dim}]{model.TurretsAlive} turrets[/color]";

        string invasion;
        if (inv == null)
        {
            Invasion last = d.Base(InvasionController.HomeId).History.LastOrDefault();
            double? next = d.NextInvasionIn(InvasionController.HomeId);
            string quiet = last == null ? "quiet" : $"quiet · last: {Words(last.Outcome)}";
            string nextText = next is double n ? $"next in ~{Clock(n)} of presence" : model.Online ? "none coming" : "none while offline";
            invasion = $"[color={Dim}]{quiet} · {nextText}[/color]";
        }
        else
        {
            string src = $"{inv.Source.Name} · {inv.Source.BearingDegrees:000}° {inv.Source.Compass}";
            invasion = inv.Phase switch
            {
                InvasionPhase.Incoming => $"[color={(inv.WarningLevel >= 3 ? Red : Amber)}]WARNING {inv.WarningLevel} OF 3 · T-{Clock(inv.SecondsToArrival(d.Now))}[/color] · from {src}",
                InvasionPhase.Assault => $"[color={Red}]ASSAULT[/color] · {inv.Force?.Tier} from {src}",
                InvasionPhase.Withdrawing => $"[color={Amber}]WITHDRAWING[/color] · {Words(inv.Outcome)}",
                _ => Words(inv.Outcome),
            };
            if (inv.Phase == InvasionPhase.Incoming && inv.OnLongRangeScan && inv.Force != null)
                invasion += $"\n               [color={Dim}]on scan: {inv.Force.Tier}, {inv.Force.Troops} troops in {inv.Force.VehicleSummary(d.Data)}[/color]";
        }

        bool siren = c.View.Sirens.On;
        string sirenText = siren ? $"[color={Red}]SOUNDING[/color]" : $"[color={Dim}]off[/color]";
        string invaders = c.InvadersRemaining == 0
            ? $"[color={Dim}]none[/color]"
            : $"[color={Red}]{c.InvadersRemaining}[/color]  [color={Dim}]({c.TroopsOnFoot} on foot, {c.CrewAndAboard} crew and aboard)[/color]";

        int storage = model.Loot.Storage.Total, crates = model.Loot.CrateTotal, total = model.Loot.TotalItems;
        int crateCount = model.Loot.Crates.Count;
        string loot = crateCount == 0
            ? $"{total} items [color={Dim}]in the storage net[/color]"
            : $"{total} items [color={Dim}]= {storage} in storage + {crates} in {crateCount} spill crate{(crateCount == 1 ? "" : "s")}[/color]";

        string garrison = c.Garrison.Destroyed ? $"[color={Green}]{c.Garrison.DisplayName} destroyed[/color]" : $"{c.Garrison.DisplayName} [color={Dim}]active, 600 m NE[/color]";

        return $"[color={Amber}]BASE[/color]       {baseState}\n" +
               $"[color={Amber}]INVASION[/color]  {invasion}\n" +
               $"[color={Amber}]SIREN[/color]      {sirenText}\n" +
               $"[color={Amber}]INVADERS[/color]  {invaders}\n" +
               $"[color={Amber}]STORAGE[/color]   {loot}\n" +
               $"[color={Amber}]SOURCE[/color]    {garrison}";
    }

    static string Words(InvasionOutcome outcome) => outcome switch
    {
        InvasionOutcome.Victory => "repelled",
        InvasionOutcome.CoreDestroyed => "core destroyed",
        InvasionOutcome.CalledOff => "called off",
        InvasionOutcome.Abandoned => "nobody home",
        _ => "under way",
    };

    public static string Clock(double seconds)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
        return $"{s / 60:00}:{s % 60:00}";
    }
}

/// <summary>
/// Long-range scan, north up, base in the middle. Shows the garrison, the incoming force once the
/// second warning puts it on scans, and the vehicles and troops once they arrive.
/// </summary>
public partial class ScanDisplay : Control
{
    public InvasionController Controller;
    public Player Player;
    const float RangeMetres = 900f;
    float _sweep;

    public override void _Process(double delta) => _sweep += (float)delta * 1.6f;

    public override void _Draw()
    {
        if (Controller == null) return;
        var centre = new Vector2(Size.X / 2, Size.Y / 2 + 10);
        float radius = Size.X / 2 - 6;
        Font font = Palette.Font;
        Color ink = new(1f, 0.91f, 0.76f);

        DrawCircle(centre, radius, new Color(0.03f, 0.07f, 0.08f, 0.78f));
        foreach (float ring in new[] { 0.33f, 0.66f, 1f })
            DrawArc(centre, radius * ring, 0, Mathf.Tau, 48, new Color(Palette.Teal, 0.45f), 1.2f);
        DrawLine(centre, centre + new Vector2(Mathf.Sin(_sweep), -Mathf.Cos(_sweep)) * radius, new Color(Palette.Teal, 0.6f), 2f);
        DrawString(font, centre + new Vector2(-5, -radius + 14), "N", HorizontalAlignment.Left, -1, 13, ink);
        DrawString(font, new Vector2(4, 12), "LONG-RANGE SCAN · 900 m", HorizontalAlignment.Left, -1, 13, ink);

        Vector2 Map(Vector3 world, out bool clipped)
        {
            var p = new Vector2(world.X, world.Z) / RangeMetres * radius;
            clipped = p.Length() > radius - 4;
            if (clipped) p = p.Normalized() * (radius - 4);
            return centre + p;
        }

        // The base and the player.
        DrawRect(new Rect2(centre - new Vector2(4, 4), new Vector2(8, 8)), Palette.Cream);
        Vector2 me = Map(Player.GlobalPosition, out _);
        DrawCircle(me, 3f, Palette.Yellow);

        // The garrison.
        Garrison g = Controller.Garrison;
        Vector2 gp = Map(g.GlobalPosition, out _);
        if (g.Destroyed)
        {
            DrawLine(gp - new Vector2(5, 5), gp + new Vector2(5, 5), Palette.Bone, 2f);
            DrawLine(gp - new Vector2(5, -5), gp + new Vector2(5, -5), Palette.Bone, 2f);
        }
        else DrawRect(new Rect2(gp - new Vector2(5, 5), new Vector2(10, 10)), Palette.Oxblood);
        DrawString(font, gp + new Vector2(8, 4), g.Destroyed ? "OKKAR-9 (down)" : "OKKAR-9", HorizontalAlignment.Left, -1, 12, ink);

        // The incoming force on long-range scan.
        Invasion inv = Controller.Current;
        if (inv != null && Controller.Director.ScanPosition(inv) is GroundPos sp)
        {
            Vector2 p = Map(new Vector3((float)sp.X, 0, (float)sp.Z), out bool clipped);
            float blink = 0.6f + 0.4f * Mathf.Sin(_sweep * 4f);
            DrawCircle(p, 7f, new Color(Palette.SirenRed, blink));
            DrawString(font, p + new Vector2(-30, -10), clipped ? "VHARN >" : "VHARN", HorizontalAlignment.Left, -1, 12, Palette.SirenRed);
        }

        foreach (IInvaderVehicle v in Controller.World.Vehicles.Where(v => !v.IsGone && IsInstanceValid(v.Body)))
            DrawRect(new Rect2(Map(v.Body.GlobalPosition, out _) - new Vector2(3.5f, 3.5f), new Vector2(7, 7)), v.IsStranded ? Palette.Bone : Palette.SirenRed);
        foreach (IInvaderTroop t in Controller.World.Troops.Where(t => t.IsAlive && !t.IsGone && IsInstanceValid(t.Body)))
            DrawCircle(Map(t.Body.GlobalPosition, out _), 1.8f, Palette.Acid);
    }
}
