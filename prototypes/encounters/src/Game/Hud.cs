using System.Collections.Generic;
using Encounters.Director;
using Godot;

namespace Encounters;

/// <summary>
/// The flight HUD: where you are and what the zone is, contact brackets on nearby crewed ships,
/// signal leads as fuzzy markers with a direction and a distance, the touch prompt and toasts.
/// A debug-styled overlay for the prototype, not the diegetic UI.
/// </summary>
public partial class Hud : Control
{
    public Main Main;

    /// <summary>What E would touch right now, if anything.</summary>
    public WorldEntity TouchTarget { get; private set; }

    public static readonly Font Font = new SystemFont { FontNames = new[] { "Bahnschrift", "DIN Alternate", "Arial Narrow", "Sans-Serif" } };
    public static readonly Font Mono = new SystemFont { FontNames = new[] { "Consolas", "Menlo", "DejaVu Sans Mono", "Monospace" } };

    /// <summary>
    /// Brackets only within sensor range. Beyond it a lead is all you get, so a distress call
    /// stays a search, not a waypoint.
    /// </summary>
    public const float SensorRange = 2500f;

    Label _title, _status, _help, _prompt;
    VBoxContainer _toasts;
    readonly List<(Label Label, double Until)> _toastLabels = new();
    readonly List<(EntityView View, float Distance)> _contacts = new();
    readonly List<Rect2> _labelled = new();

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _title = MakeLabel(26, new Vector2(22, 14));
        _status = MakeLabel(16, new Vector2(24, 50));
        _help = MakeLabel(14, Vector2.Zero);
        Pin(_help, 0f, 1f, 24, -36, 1400, -12);
        _help.Modulate = new Color(1, 1, 1, 0.75f);
        _help.Text = "W/S throttle  ·  mouse or arrows steer  ·  A/D roll  ·  Shift boost  ·  E touch / claim wreck  ·  C drop cargo  ·  J jump zone  ·  Tab director  ·  M map  ·  V view  ·  Esc mouse";

        _prompt = MakeLabel(19, Vector2.Zero);
        _prompt.HorizontalAlignment = HorizontalAlignment.Center;
        Pin(_prompt, 0.5f, 1f, -500, -120, 500, -90);

        _toasts = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_toasts);
        Pin(_toasts, 0.5f, 0f, -420, 18, 420, 40);
    }

    /// <summary>Anchors a control at one point of the screen with fixed offsets from it.</summary>
    static void Pin(Control c, float anchorX, float anchorY, float left, float top, float right, float bottom)
    {
        c.AnchorLeft = c.AnchorRight = anchorX;
        c.AnchorTop = c.AnchorBottom = anchorY;
        c.OffsetLeft = left;
        c.OffsetTop = top;
        c.OffsetRight = right;
        c.OffsetBottom = bottom;
    }

    Label MakeLabel(int size, Vector2 at)
    {
        var label = new Label { Position = at, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", Font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Palette.Text);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("outline_size", 6);
        AddChild(label);
        return label;
    }

    public void Toast(string text, Color color, double seconds = 5)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", Font);
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        label.AddThemeConstantOverride("outline_size", 7);
        _toasts.AddChild(label);
        _toastLabels.Add((label, Time.GetTicksMsec() / 1000.0 + seconds));
        while (_toastLabels.Count > 4) DropToast(0);
    }

    void DropToast(int i)
    {
        _toastLabels[i].Label.QueueFree();
        _toastLabels.RemoveAt(i);
    }

    public override void _Process(double delta)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        for (int i = _toastLabels.Count - 1; i >= 0; i--)
        {
            double left = _toastLabels[i].Until - now;
            if (left <= 0) DropToast(i);
            else _toastLabels[i].Label.Modulate = new Color(1, 1, 1, (float)Mathf.Min(1.0, left));
        }

        ZoneDef zone = Main.Zone;
        EncounterDirector d = Main.Director;
        Vector3 at = Main.Camera.GlobalPosition;
        var factions = new List<string>();
        zone.FactionsAt(at.ToNum(), 0f, factions);
        string territory = factions.Count == 0 ? "unclaimed space"
            : string.Join(" + ", factions.ConvertAll(f => d.Deck.FactionName(f))) + " territory";
        int players = d.Instance(zone.Id)?.Players.Count ?? 0;
        _title.Visible = _status.Visible = !Main.Overlay.Visible; // the overlay says it all, and more
        _title.Text = zone.Name.ToUpperInvariant();
        _status.Text =
            $"{zone.Kind} · {zone.Region} · {zone.Star}-class star · danger {zone.Danger}/5\n" +
            $"{territory}\n" +
            $"{Main.Craft.Speed:F0} m/s   ·   players here {players}   ·   {Engine.GetFramesPerSecond()} fps";

        UpdateTouchTarget(d, at);
        QueueRedraw();
    }

    void UpdateTouchTarget(EncounterDirector d, Vector3 at)
    {
        TouchTarget = null;
        float best = d.Config.TouchDistance;
        foreach (EntityView view in Main.Views.Values)
        {
            WorldEntity e = view.Entity;
            if (e.Kind != EntityKind.NpcShip) continue;
            float dist = e.Position.ToGodot().DistanceTo(at); // the director's position, as Touch uses
            if (dist > best) continue;
            best = dist;
            TouchTarget = e;
        }

        if (TouchTarget == null)
        {
            _prompt.Text = "";
            return;
        }
        WorldEntity t = TouchTarget;
        string what = Describe(t, d);
        if (t.Touch != TouchKind.None)
        {
            _prompt.AddThemeColorOverride("font_color", Palette.Lasting);
            _prompt.Text = t.Touch == TouchKind.Claimed
                ? $"{what}: claimed by P{t.TouchedBy}, theirs for good"
                : $"{what}: touched by P{t.TouchedBy}, a permanent record (still its crew's)";
            return;
        }
        // Only a wreck can be claimed. A live ship is yours only by boarding it and hijacking its
        // core, which this prototype doesn't have, so E just marks it.
        _prompt.AddThemeColorOverride("font_color", Palette.Text);
        _prompt.Text = t.Wrecked
            ? $"[E]  Claim salvage: wreck of a {what}, {t.Crew} aboard"
            : $"[E]  Touch: {what}, crew {t.Crew}. It stays theirs and becomes a permanent record";
    }

    public static string Describe(WorldEntity e, EncounterDirector d) => e.Kind switch
    {
        EntityKind.PlayerCargo => "Your cargo pod",
        EntityKind.PlayerShip => "Player ship",
        _ => $"{d.Deck.FactionName(e.Faction)} {HullName(e.Hull)}",
    };

    static string HullName(string hull) => hull switch
    {
        "trader" => "merchantman",
        "miner" => "mining rig",
        "bloom-hulk" => "hulk",
        "vharn-gunship" => "gunship",
        "vharn-capital" => "capital ship",
        "homestead-hauler" => "hauler",
        _ => hull.Replace('-', ' '),
    };

    public override void _Draw()
    {
        Camera3D cam = Main.Camera;
        EncounterDirector d = Main.Director;
        if (cam == null || d == null) return;
        Vector2 screen = GetViewportRect().Size;
        Vector3 eye = cam.GlobalPosition;
        float pixelsPerRadian = screen.Y * 0.5f / Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f);

        // Contacts: brackets on everything within a few kilometres, labels on the nearest few.
        _contacts.Clear();
        foreach (EntityView view in Main.Views.Values)
        {
            float dist = view.GlobalPosition.DistanceTo(eye);
            if (dist < SensorRange && !cam.IsPositionBehind(view.GlobalPosition)) _contacts.Add((view, dist));
        }
        _contacts.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        _labelled.Clear();
        for (int i = 0; i < _contacts.Count; i++)
        {
            var (view, dist) = _contacts[i];
            Vector2 p = cam.UnprojectPosition(view.GlobalPosition);
            if (p.X < -50 || p.Y < -50 || p.X > screen.X + 50 || p.Y > screen.Y + 50) continue;
            WorldEntity e = view.Entity;
            Color color = e.IsLasting ? Palette.Lasting : Palette.Faction(e.Faction);
            float r = Mathf.Clamp(view.Hull.Radius / dist * pixelsPerRadian, 7f, 90f);
            bool target = e == TouchTarget;
            Bracket(p, r + (target ? 5f : 0f), color, target ? 3f : 1.6f);
            Vector2 at = p + new Vector2(r + 8f, -r + 4f);
            var box = new Rect2(at - new Vector2(0, 14), new Vector2(230, 34));
            if (!target && (_labelled.Count >= 5 || _labelled.Exists(b => b.Intersects(box)))) continue; // never pile labels up
            _labelled.Add(box);

            string head = e.Wrecked ? $"WRECK · {Describe(e, d)}" : Describe(e, d).ToUpperInvariant();
            string tag = e.Touch != TouchKind.None ? $"{e.Touch.ToString().ToUpperInvariant()} · permanent"
                : e.PlayerOwned ? "yours · permanent"
                : e.Wrecked ? $"salvage · {e.Crew} aboard"
                : $"crew {e.Crew}{(e.Hostile ? " · hostile event" : "")}";
            Text(at, head, 14, color);
            Text(at + new Vector2(0, 17), $"{tag} · {Km(dist)}", 13, new Color(color, 0.8f));
        }

        // Signal leads: a fuzzy circle where the lead points, never the exact spot.
        foreach (SignalLead lead in d.LeadsFor(Main.LocalPlayer))
        {
            Vector3 estimate = lead.Estimate.ToGodot();
            float dist = estimate.DistanceTo(eye);
            Color color = Palette.Lead(lead.Kind);
            string head = lead.Kind == "distress" ? "DISTRESS CALL" : "TRANSMISSION";
            string sub = lead.Kind == "distress" ? $"~{Km(dist)} · search the circle" : $"{lead.Label} · ~{Km(dist)}";
            Vector2 p = cam.UnprojectPosition(estimate);
            bool onScreen = !cam.IsPositionBehind(estimate) && p.X > 40 && p.Y > 40 && p.X < screen.X - 40 && p.Y < screen.Y - 40;
            if (onScreen)
            {
                float fuzz = lead.Distance * Mathf.Sin(Mathf.DegToRad(d.Config.Lead.AngleFuzzDeg));
                float r = Mathf.Clamp(fuzz / Mathf.Max(dist, 1f) * pixelsPerRadian, 18f, 260f);
                DashedCircle(p, r, color);
                Diamond(p, 9f, color);
                Text(p + new Vector2(r * 0.72f + 10f, -r * 0.72f), head, 17, color);
                Text(p + new Vector2(r * 0.72f + 10f, -r * 0.72f + 20f), sub, 14, new Color(color, 0.85f));
            }
            else EdgeArrow(cam, estimate, screen, color, head, sub);
        }

        // A crosshair.
        Vector2 c = screen * 0.5f;
        DrawLine(c + new Vector2(-12, 0), c + new Vector2(-4, 0), new Color(1, 1, 1, 0.7f), 1.5f);
        DrawLine(c + new Vector2(4, 0), c + new Vector2(12, 0), new Color(1, 1, 1, 0.7f), 1.5f);
        DrawLine(c + new Vector2(0, -12), c + new Vector2(0, -4), new Color(1, 1, 1, 0.7f), 1.5f);
        DrawLine(c + new Vector2(0, 4), c + new Vector2(0, 12), new Color(1, 1, 1, 0.7f), 1.5f);
    }

    /// <summary>An arrow pinned to the screen edge, pointing the way to turn.</summary>
    void EdgeArrow(Camera3D cam, Vector3 target, Vector2 screen, Color color, string head, string sub)
    {
        Vector3 local = cam.GlobalTransform.AffineInverse() * target;
        var dir = new Vector2(local.X, -local.Y);
        if (dir.LengthSquared() < 1e-4f) dir = Vector2.Down;
        dir = dir.Normalized();
        Vector2 centre = screen * 0.5f, half = centre - new Vector2(70, 70);
        float scale = Mathf.Min(Mathf.Abs(half.X / Mathf.Max(Mathf.Abs(dir.X), 1e-4f)), Mathf.Abs(half.Y / Mathf.Max(Mathf.Abs(dir.Y), 1e-4f)));
        Vector2 tip = centre + dir * scale;
        Vector2 side = new(-dir.Y, dir.X);
        DrawColoredPolygon(new[] { tip + dir * 16f, tip - dir * 8f + side * 11f, tip - dir * 8f - side * 11f }, color);
        Vector2 at = tip - dir * 30f + new Vector2(dir.X > 0 ? -230f : 14f, dir.Y > 0 ? -40f : 10f);
        Text(at, head, 16, color);
        Text(at + new Vector2(0, 19), sub, 13, new Color(color, 0.85f));
    }

    void Bracket(Vector2 p, float r, Color color, float width)
    {
        float l = Mathf.Max(5f, r * 0.4f);
        foreach (var (sx, sy) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            Vector2 corner = p + new Vector2(sx * r, sy * r);
            DrawLine(corner, corner - new Vector2(sx * l, 0), color, width);
            DrawLine(corner, corner - new Vector2(0, sy * l), color, width);
        }
    }

    void Diamond(Vector2 p, float r, Color color) =>
        DrawPolyline(new[] { p + new Vector2(0, -r), p + new Vector2(r, 0), p + new Vector2(0, r), p + new Vector2(-r, 0), p + new Vector2(0, -r) }, color, 2f);

    void DashedCircle(Vector2 p, float r, Color color)
    {
        const int dashes = 28;
        for (int i = 0; i < dashes; i++)
        {
            float a = i * Mathf.Tau / dashes;
            DrawArc(p, r, a, a + Mathf.Tau / dashes * 0.55f, 4, color, 2f);
        }
        DrawCircle(p, r, new Color(color, 0.07f));
    }

    void Text(Vector2 at, string text, int size, Color color)
    {
        DrawStringOutline(Font, at, text, HorizontalAlignment.Left, -1, size, 5, new Color(0, 0, 0, 0.8f));
        DrawString(Font, at, text, HorizontalAlignment.Left, -1, size, color);
    }

    public static string Km(float metres) => metres >= 1000f ? $"{metres / 1000f:F1} km" : $"{metres:F0} m";
}
