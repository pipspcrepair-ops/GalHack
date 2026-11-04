using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Encounters.Director;
using Godot;

namespace Encounters;

/// <summary>
/// The Tab overlay: the director's state for the zone you are in. Active events, the deck's
/// weights where you are and why cards are blocked, budget and caps, cooldowns, leads with
/// their hidden truth, and the lasting records.
/// </summary>
public partial class DirectorOverlay : PanelContainer
{
    public Main Main;

    RichTextLabel _text;
    double _nextRefresh;
    readonly List<WeightedEvent> _weights = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AnchorTop = 0;
        AnchorBottom = 1;
        OffsetLeft = 16;
        OffsetTop = 16;
        OffsetRight = 16 + 660;
        OffsetBottom = -16;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.04f, 0.08f, 0.86f),
            BorderColor = new Color("f4a100"),
            BorderWidthLeft = 4,
            ContentMarginLeft = 18, ContentMarginRight = 14, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        _text = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollActive = false,
            FitContent = false,
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None,
        };
        _text.AddThemeFontOverride("normal_font", Hud.Mono);
        _text.AddThemeFontOverride("bold_font", Hud.Mono);
        _text.AddThemeFontSizeOverride("normal_font_size", 13);
        _text.AddThemeFontSizeOverride("bold_font_size", 13);
        _text.AddThemeColorOverride("default_color", Palette.Text);
        AddChild(_text);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (now < _nextRefresh) return;
        _nextRefresh = now + 0.25;
        _text.Text = Build();
    }

    string Build()
    {
        EncounterDirector d = Main.Director;
        ZoneDef zone = Main.Zone;
        ZoneInstance zi = d.Instance(zone.Id);
        double t = d.Time;
        var at = Main.Camera.GlobalPosition.ToNum();
        var sb = new StringBuilder();

        Head(sb, "ENCOUNTER DIRECTOR");
        sb.AppendLine($"seed {d.Seed} · sim {t:F1} s · {d.Config.TickHz:F0} Hz · tick avg {d.Stats.AverageMicros:F0} µs, max {d.Stats.MaxMicros:F0} µs");
        sb.AppendLine($"zone [b]{zone.Id}[/b] · {zone.Kind} · {zone.Region} · star {zone.Star} · danger {zone.Danger} · visit {zi?.Visit}");
        sb.AppendLine(Dim("tags " + string.Join(" ", zone.AllTags.OrderBy(x => x))));
        if (zi == null) return sb.ToString();

        Head(sb, "BUDGET");
        sb.AppendLine($"players {zi.Players.Count}   events {Bar(zi.Encounters.Count, zi.MaxEvents)} {zi.Encounters.Count}/{zi.MaxEvents}   ships {Bar(zi.Ships.Count, zi.MaxShips)} {zi.Ships.Count}/{zi.MaxShips}");
        var caps = d.Config.CategoryCaps.Keys.Select(c => $"{c} {zi.CategoryCount(c)}/{d.Config.CategoryCap(c)}");
        sb.AppendLine(Dim("caps  " + string.Join("  ", caps)));
        sb.AppendLine($"next spawn in {Math.Max(0, zi.NextSpawnAt - t):F1} s · spawned {zi.Spawns} · placement retries {zi.PlacementFailures}");

        Head(sb, "COOLDOWNS");
        var cooling = zi.CategoryReadyAt.Where(kv => kv.Value > t).Select(kv => $"{kv.Key} {kv.Value - t:F0}s").ToList();
        var repeats = zi.LastSpawnOf.Where(kv => kv.Value + d.Config.NoRepeatWindow > t)
            .Select(kv => $"{kv.Key} {kv.Value + d.Config.NoRepeatWindow - t:F0}s").ToList();
        sb.AppendLine("category  " + (cooling.Count > 0 ? string.Join(" · ", cooling) : Dim("none")));
        sb.AppendLine("no-repeat " + (repeats.Count > 0 ? string.Join(" · ", repeats) : Dim("none")));

        Head(sb, "DECK WEIGHTS HERE");
        zi.WeightsAt(at, _weights);
        float hostile = _weights.Where(w => w.Def.Hostile).Sum(w => w.Share);
        var factions = new List<string>();
        zone.FactionsAt(at, 0f, factions);
        sb.AppendLine(Dim($"territory: {(factions.Count == 0 ? "unclaimed" : string.Join(", ", factions))} · hostile share {hostile:P0} (danger {zone.Danger})"));
        foreach (WeightedEvent w in _weights.OrderByDescending(w => w.Share))
        {
            string state = w.Gate != Gate.Open ? $"gated: {GateText(w, d.Deck)}" : zi.BlockReason(w.Def, t) ?? "ready";
            string line = $"{w.Def.Id,-18} {Bar(w.Share, 1f, 12)} {w.Share,4:P0}  {(w.Def.Hostile ? "H" : " ")} {state}";
            sb.AppendLine(w.Gate != Gate.Open ? Dim(line) : state == "ready" ? line : $"[color=#f4a261]{line}[/color]");
        }

        Head(sb, $"ACTIVE EVENTS ({zi.Encounters.Count})");
        foreach (Encounter enc in zi.Encounters)
        {
            int live = enc.LiveShips, wrecked = enc.Ships.Count(s => s.Wrecked), crew = enc.Ships.Where(s => s.Live).Sum(s => s.Crew);
            float dist = (float)enc.Ships.Where(s => s.Live).Select(s => (double)System.Numerics.Vector3.Distance(s.Position, at)).DefaultIfEmpty(0).Min();
            string state = enc.State == EncounterState.Active ? $"{t - enc.Start:F0}/{enc.End - enc.Start:F0}s" : "leaving";
            string sides = enc.SideA != null ? $" {enc.SideA} v {enc.SideB}" : "";
            sb.AppendLine($"#{enc.Id,-4} {enc.Def.Id,-18} {state,-9} {live} ships, crew {crew}{(wrecked > 0 ? $", {wrecked} lost" : "")} · {Hud.Km(dist)}{sides}{(enc.Touched ? " [color=#9bf26b]touched[/color]" : "")}");
        }

        List<SignalLead> leads = d.LeadsFor(Main.LocalPlayer).ToList();
        if (leads.Count > 0)
        {
            Head(sb, "LEADS");
            foreach (SignalLead lead in leads)
                sb.AppendLine($"{lead.Kind,-12} ~{Hud.Km(lead.Distance),-8} {Dim($"truth {Hud.Km(lead.TrueOffset.Length())}, off by {lead.AngleErrorDeg:F1}° (debug only)")}");
        }

        IReadOnlyList<WorldEntity> records = d.Records.InZone(zone.Id);
        Head(sb, $"PERMANENT RECORDS ({records.Count} here, {d.Records.Count} in the world)");
        foreach (WorldEntity e in records.Reverse().Take(7))
        {
            string kind = e.PlayerOwned && e.Touch == TouchKind.None ? "owned" : e.Touch != TouchKind.None ? e.Touch.ToString().ToLowerInvariant() : "wreck";
            string from = e.EventId != "" ? $" · from {e.EventId}" : "";
            sb.AppendLine($"[color=#9bf26b]#{e.Id,-4} {kind,-8}[/color] {Hud.Describe(e, d),-30}{Dim(from)}");
        }
        if (records.Count > 7) sb.AppendLine(Dim($"… and {records.Count - 7} more"));
        if (d.Records.GuardTrips > 0) sb.AppendLine($"[color=#ff5d3a]cleanup guard tripped {d.Records.GuardTrips}x[/color]");

        Head(sb, "LOG");
        foreach (LogEntry e in d.RecentLog.Where(e => e.Zone == zone.Id && e.Kind != "despawn").Reverse().Take(6))
            sb.AppendLine(Dim($"{e.Time,7:F1} {e.Kind,-9} {e.Text}"));
        return sb.ToString();
    }

    static string GateText(WeightedEvent w, EventDeck deck) => w.Gate switch
    {
        Gate.ZoneTags => "zone tags",
        Gate.Danger => $"danger {w.Def.Danger[0]}-{w.Def.Danger[1]}",
        Gate.NoPoi => $"no {w.Def.Anchor}",
        Gate.Territory => $"not in {deck.FactionName(w.Def.Faction)} space",
        Gate.Border => "no border here",
        _ => "",
    };

    static void Head(StringBuilder sb, string title) => sb.AppendLine($"\n[color=#f4a100][b]{title}[/b][/color]");

    static string Dim(string text) => $"[color=#ffe8c288]{text}[/color]";

    static string Bar(float value, float max, int width = 8)
    {
        int filled = max <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(value / max * width), 0, width);
        return new string('█', filled) + new string('░', width - filled);
    }
}
