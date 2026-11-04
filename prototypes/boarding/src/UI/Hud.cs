using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Boarding;

/// <summary>
/// A deliberately plain debug-style HUD: one status block (health, ammo, alarm, hijack
/// stages, self-destruct, ship state, inventory), a crosshair with hit markers and a kill
/// confirmation, the current interaction prompt with a progress bar, a short message feed,
/// and a red damage vignette that becomes a heartbeat in last stand.
/// </summary>
public partial class Hud : CanvasLayer
{
    public Main Game;

    Label _status, _prompt, _banner, _countdown, _feed, _warning;
    Crosshair _crosshair;
    ColorRect _progressBack, _progressFill;
    TextureRect _vignette;
    readonly List<(string text, float age)> _messages = new();
    string _bannerText = "";
    float _bannerAge = 99f, _damage, _time;

    public string StatusText => _status.Text;
    public string BannerText => _bannerText;

    public override void _Ready()
    {
        _vignette = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient { Offsets = new[] { 0.45f, 1f }, Colors = new[] { new Color(0.8f, 0, 0, 0), new Color(0.8f, 0, 0, 0.85f) } },
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(1.05f, 0.5f),
                Width = 256,
                Height = 256,
            },
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        _vignette.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_vignette);

        _status = MakeLabel(17, new Color("ffe8c2"));
        _status.Position = new Vector2(16, 12);

        _crosshair = new Crosshair();
        _crosshair.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _crosshair.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_crosshair);

        _prompt = Centered(MakeLabel(18, Colors.White), 60f);
        _progressBack = new ColorRect { Color = new Color(0, 0, 0, 0.55f), Size = new Vector2(240, 8), MouseFilter = Control.MouseFilterEnum.Ignore };
        _progressFill = new ColorRect { Color = Palette.TealGlow, Size = new Vector2(0, 8), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_progressBack);
        AddChild(_progressFill);

        _banner = Centered(MakeLabel(30, new Color("ffd27a")), -220f);
        _countdown = Centered(MakeLabel(34, Palette.AlarmRed), -300f);
        _warning = Centered(MakeLabel(22, Palette.AlarmRed), 110f);
        _feed = MakeLabel(16, new Color("ffe8c2"));
        _feed.AnchorTop = _feed.AnchorBottom = 1f;
        _feed.OffsetTop = -150f;
        _feed.OffsetLeft = 16f;
    }

    Label MakeLabel(int size, Color color)
    {
        var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", 6);
        AddChild(label);
        return label;
    }

    static Label Centered(Label label, float y)
    {
        label.AnchorLeft = 0f;
        label.AnchorRight = 1f;
        label.AnchorTop = label.AnchorBottom = 0.5f;
        label.OffsetTop = y;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        return label;
    }

    // ---- Events from the game ------------------------------------------------------------

    public void Message(string text) => _messages.Add((text, 0f));

    public void Banner(string text)
    {
        _bannerText = text;
        _bannerAge = 0f;
    }

    public void HitMarker(bool head, bool kill) => _crosshair.Hit(head, kill);

    public void DamageFlash(float fraction) => _damage = Mathf.Min(1f, _damage + 0.35f + fraction * 2f);

    public void DamageFrom(Vector3 from, Transform3D view)
    {
        Vector3 local = view.Basis.Inverse() * (from - view.Origin);
        _crosshair.DamageFrom(Mathf.Atan2(local.X, -local.Z));
    }

    // ---- Per frame -----------------------------------------------------------------------

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        Player player = Game.Player;
        if (player == null) return;

        _status.Text = Status(player);
        _crosshair.Spread = player.Current.Def.Pellets > 1 ? 18f : 8f;

        // Interaction prompt and hold progress.
        _prompt.Text = player.Prompt;
        var size = GetViewport().GetVisibleRect().Size;
        bool showBar = player.ActionProgress >= 0f && !string.IsNullOrEmpty(player.Prompt);
        _progressBack.Visible = _progressFill.Visible = showBar;
        _progressBack.Position = new Vector2(size.X * 0.5f - 120f, size.Y * 0.5f + 92f);
        _progressFill.Position = _progressBack.Position;
        _progressFill.Size = new Vector2(240f * Mathf.Clamp(player.ActionProgress, 0f, 1f), 8f);
        _progressFill.Color = Game.Hijack.Paused && player.ChannellingCore ? Palette.AlarmRed : Palette.TealGlow;

        // Banner, self-destruct, last-stand visor warning.
        _bannerAge += dt;
        _banner.Text = _bannerAge < 5f ? _bannerText : "";
        _banner.Modulate = new Color(1, 1, 1, Mathf.Clamp(5f - _bannerAge, 0f, 1f));
        var sd = Game.Alarm.SelfDestruct;
        _countdown.Text = sd.Active ? $"SELF-DESTRUCT  {(int)sd.Remaining / 60:00}:{(int)sd.Remaining % 60:00}" : "";
        _countdown.Modulate = new Color(1, 1, 1, sd.Remaining < 20f ? 0.6f + 0.4f * Mathf.Sin(_time * 12f) : 1f);

        bool lastStand = player.Health.InLastStand;
        _warning.Text = player.Health.Dead ? "DOWN" : lastStand ? "VISOR WARNING: LAST STAND - BREAK AWAY" : player.Health.Low ? "HEALTH CRITICAL" : "";

        // Red vignette: hit flashes, plus a heartbeat while health is low.
        _damage = Mathf.MoveToward(_damage, 0f, dt * 1.8f);
        float heartbeat = 0f;
        if (player.Health.Low && !player.Health.Dead)
        {
            float beat = (_time * (lastStand ? 2.2f : 1.3f)) % 1f;
            heartbeat = (beat < 0.12f ? 1f - beat / 0.12f : 0f) + (beat is > 0.22f and < 0.32f ? 0.6f : 0f);
            heartbeat = 0.25f + 0.45f * heartbeat;
        }
        _vignette.Modulate = new Color(1, 1, 1, Mathf.Clamp(Mathf.Max(_damage, heartbeat), 0f, 1f));

        for (int i = _messages.Count - 1; i >= 0; i--)
        {
            var (text, age) = _messages[i];
            _messages[i] = (text, age + dt);
            if (age > 6f) _messages.RemoveAt(i);
        }
        _feed.Text = string.Join("\n", _messages.TakeLast(5).Select(m => m.text));
    }

    string Status(Player player)
    {
        var h = player.Health;
        Weapon w = player.Current;
        string ammo = w.Reloading ? "reloading..." : $"{w.Ammo} / {w.Reserve}";
        string hp = $"HEALTH {Mathf.CeilToInt(h.Current)} / {h.Max:0}" + (h.InLastStand ? "   LAST STAND" : "");

        AlarmLevel level = Game.Alarm.Level;
        string alarm = Game.Ship.Hostile
            ? $"ALARM {(int)level}  {level switch { AlarmLevel.Unaware => "unaware", AlarmLevel.Alert => "ALERT", AlarmLevel.Lockdown => "LOCKDOWN", _ => "LAST STAND" }}"
            : "ALARM  -";

        HijackSystem hj = Game.Hijack;
        string bars = string.Join(" ", Enumerable.Range(0, hj.Stages).Select(s => Bar(hj.StageFill(s))));
        string hijack = hj.Complete ? "HIJACK  complete"
            : $"HIJACK  {bars}  stage {Mathf.Min(hj.CompletedStages + 1, hj.Stages)}/{hj.Stages}" + (hj.Paused && hj.Channelling ? "  STALLED (taking fire)" : "");

        Ship ship = Game.Ship;
        string state = Game.Exploded ? "SHIP: destroyed"
            : ship.OwnerFaction == Faction.Player ? (ship.Rebooting ? $"THE SHIP IS YOURS   weapons + warp rebooting {ship.RebootRemaining:0}s" : "THE SHIP IS YOURS   weapons + warp online")
            : ship.Derelict ? "SHIP: derelict (core destroyed) - salvage its components"
            : $"SHIP: {ship.Layout.Name}, hostile   turret {(ship.Turret != null && ship.Turret.Hostile ? "manned" : "silent")}";

        int guards = Game.Guards.Count(g => !g.Dead);
        return $"{hp}\n{w.Def.Name}  {ammo}\n{alarm}   guards aboard {guards}\n{hijack}\n{state}\n" +
               $"Inventory: {Game.Inventory}";
    }

    static string Bar(float fill)
    {
        int n = Mathf.RoundToInt(fill * 6f);
        return "[" + new string('#', n) + new string('.', 6 - n) + "]";
    }
}

/// <summary>Crosshair, hit markers (white body, yellow head, red kill) and damage direction ticks.</summary>
public partial class Crosshair : Control
{
    public float Spread = 8f;
    float _hit, _kill;
    bool _head;
    float _damageAngle, _damageAge = 9f;

    public void Hit(bool head, bool kill)
    {
        _hit = 0.16f;
        _head = head;
        if (kill) _kill = 0.55f;
    }

    public void DamageFrom(float angle)
    {
        _damageAngle = angle;
        _damageAge = 0f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _hit = Mathf.Max(0f, _hit - dt);
        _kill = Mathf.Max(0f, _kill - dt);
        _damageAge += dt;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 c = Size * 0.5f;
        var white = new Color(1, 1, 1, 0.85f);
        DrawCircle(c, 1.6f, white);
        foreach (Vector2 d in new[] { Vector2.Right, Vector2.Left, Vector2.Up, Vector2.Down })
            DrawLine(c + d * Spread, c + d * (Spread + 7f), white, 2f);

        if (_hit > 0f || _kill > 0f)
        {
            bool kill = _kill > 0f;
            Color color = kill ? new Color("ff3b30") : _head ? new Color("ffd27a") : Colors.White;
            float r0 = kill ? 10f : 7f, r1 = kill ? 22f : 15f;
            float alpha = kill ? Mathf.Clamp(_kill / 0.3f, 0f, 1f) : 1f;
            foreach (Vector2 d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
                DrawLine(c + d.Normalized() * r0, c + d.Normalized() * r1, new Color(color, alpha), kill ? 3.5f : 2.5f);
            if (kill) DrawString(ThemeDB.FallbackFont, c + new Vector2(-24, 44), "KILL", HorizontalAlignment.Left, -1, 18, new Color(color, alpha));
        }

        if (_damageAge < 0.8f)
        {
            // A red chevron on a ring, pointing toward whoever hit you.
            var dir = new Vector2(Mathf.Sin(_damageAngle), -Mathf.Cos(_damageAngle));
            var side = new Vector2(-dir.Y, dir.X);
            Vector2 tip = c + dir * 120f;
            var red = new Color(1f, 0.15f, 0.1f, 1f - _damageAge / 0.8f);
            DrawColoredPolygon(new[] { tip, tip - dir * 22f + side * 26f, tip - dir * 22f - side * 26f }, red);
        }
    }
}
