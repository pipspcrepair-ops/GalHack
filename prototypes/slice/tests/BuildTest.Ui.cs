using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Items;
using Slice.Ui;
using SurvivalVitals.Model;
using Weather.Model;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The UI round: hazards as symbols with a timer and no words, each by a sign of its own and never a
/// generic one, kept off every menu page's panels by a strip of the menu's header, a compass that starts
/// with only the bunker and the place in view from its way out, the scanner's ping marking it for ten
/// seconds, every tech tab on one 1080p screen, and no two HUD labels on top of each other.
/// </summary>
public partial class BuildTest
{
    IEnumerable<int> UiRound()
    {
        GD.Print("-- the UI round --");
        foreach (int x in AlertChecks()) yield return x;
        AlertSignChecks();
        foreach (int x in ImpactChecks()) yield return x;
        foreach (int x in ScannerChecks()) yield return x;
        foreach (int x in HeldModelChecks()) yield return x;
        foreach (int x in MenuTextChecks()) yield return x;
        foreach (int x in EscapeRowChecks()) yield return x;
        HudBoxChecks();
        foreach (int x in TechFitChecks()) yield return x;
        foreach (int x in GalaxyNameChecks()) yield return x;
        foreach (int x in MarkerChecks()) yield return x;
        foreach (int x in NoticeChecks()) yield return x;
        foreach (int x in WordChecks()) yield return x;
        Check(MenuRoot.Glass <= 0.6f && _game.Menu.Root.Gui.Backing < 1f && !_game.GetTree().Paused, $"the Tab menu is see-through (glass {MenuRoot.Glass:0.00}, backings {_game.Menu.Root.Gui.Backing:0.00}) and the world runs behind it");
    }

    /// <summary>
    /// The impact warning belongs to piloting: dropped from height on foot the suit raises its fall alert (its damage rules are
    /// unchanged) and the glass shows no IMPACT; in a cockpit it shows only for a vessel coming down fast, close to the ground.
    /// </summary>
    IEnumerable<int> ImpactChecks()
    {
        var s = _game.Survivor;
        Vector3 ground = _game.Zone.GroundAt(s.WorldFeet.X, s.WorldFeet.Z);
        s.PlaceOutside((ground + Vector3.Up * 40f).ToNumerics());
        bool alerted = false, shown = false;
        float fastest = 0f;
        for (int i = 0; i < 180 && s.WorldFeet.Y > ground.Y + 8f; i++)
        {
            yield return 0;
            alerted |= (_game.Suit.Last.Warnings & Alert.Fall) != 0;
            shown |= _game.Visor.ImpactWarning;
            fastest = MathF.Max(fastest, s.ImpactSpeed);
        }
        s.PlaceOutside((ground + Vector3.Up * 0.3f).ToNumerics()); // back down before it lands
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(alerted && !shown, $"on foot, falling at up to {fastest:F0} m/s the suit raises its fall alert (its damage rules stand) and the glass shows no IMPACT");
        Check(Visor.ImpactSoon(20f, 50f, 8f, out float seconds) && seconds < Visor.ImpactSeconds
              && !Visor.ImpactSoon(6f, 10f, 8f, out _) && !Visor.ImpactSoon(20f, 400f, 8f, out _) && !Visor.ImpactSoon(-5f, 10f, 8f, out _),
            $"a pilot's impact warning needs a vessel coming down faster than it can land (8 m/s) within {Visor.ImpactSeconds:F0} s of the ground: 20 m/s at 50 m is {seconds:F1} s away, slower, higher or rising is not");
    }

    /// <summary>The galaxy map: every star drawn, names off but for the picked star, a toggle for the near ones.</summary>
    IEnumerable<int> GalaxyNameChecks()
    {
        _game.Menu.OpenMap(MapLevel.Galaxy);
        foreach (int x in _drive.Wait(4)) yield return x;
        MapLevels levels = _game.Menu.Root.Map.Levels;
        bool before = levels.LabelNear;
        foreach (int x in _drive.ClickWidget("map:names-near")) yield return x;
        Check(!before && levels.LabelNear, "the galaxy map's names start off (the picked star keeps its own), and NEAR NAMES turns the near ones on");
        foreach (int x in _drive.ClickWidget("map:names-near")) yield return x;
        _game.Menu.Close();
    }

    /// <summary>Every tech tab, every deco set: each node a tile inside the screen, none on another, none scrolled away, and tall enough to read at 1080p.</summary>
    IEnumerable<int> TechFitChecks()
    {
        TechSection tech = _game.Menu.Root.Tech;
        _game.Menu.Open(MenuTab.Tech);
        var screen = new Rect2(0, 0, Gui.Width, Gui.Height);
        const float Px = 1080f / Gui.Height; // design units to 1080p pixels
        foreach (string tab in Slice.Items.Progress.Tabs)
        {
            tech.Tab = tab;
            tech.Group = null;
            foreach (int x in _drive.Wait(3)) yield return x;
            var groups = _game.Progress.Nodes.Where(n => n.Tab == tab && n.Group != null).Select(n => n.Group).Distinct().ToList();
            foreach (string grp in groups.Count == 0 ? new string[] { null } : groups.ToArray())
            {
                if (grp != null) { tech.Group = grp; foreach (int x in _drive.Wait(3)) yield return x; }
                var want = _game.Progress.Nodes.Where(n => n.Tab == tab && (n.Group == null || n.Group == grp)).Select(n => n.Id).ToList();
                var tiles = tech.Placed;
                var missing = want.Where(id => !tiles.ContainsKey(id)).ToList();
                var outside = tiles.Where(kv => !screen.Encloses(kv.Value) || !tech.GridArea.Encloses(kv.Value)).Select(kv => kv.Key).ToList();
                var list = tiles.ToList();
                int overlaps = 0;
                for (int i = 0; i < list.Count; i++)
                    for (int k = i + 1; k < list.Count; k++)
                        if (list[i].Value.Intersects(list[k].Value)) overlaps++;
                float minH = tiles.Count == 0 ? 0 : tiles.Values.Min(r => r.Size.Y), minW = tiles.Count == 0 ? 0 : tiles.Values.Min(r => r.Size.X);
                string name = grp == null ? tab : $"{tab}, deco set {grp}";
                if (grp == null || grp == groups[0] || missing.Count + outside.Count + overlaps > 0 || minH < TechSection.MinTileH)
                    Check(missing.Count == 0 && outside.Count == 0 && overlaps == 0 && minH >= TechSection.MinTileH,
                        $"tech {name}: all {want.Count} nodes on one 1080p screen, tiles {minW * Px:F0} x {minH * Px:F0} px or larger, none overlapping{(missing.Count + outside.Count + overlaps == 0 ? "" : $": missing {missing.Count}, outside {outside.Count}, overlaps {overlaps}")}");
            }
        }
        Check(_game.Progress.Nodes.Where(n => n.Tab == "large").Select(n => TechSection.LaneOf(new TechLayout(_game.Progress.Nodes.Where(m => m.Tab == "large")).ById[n.Id])).Distinct().Count() == 3,
            "the Large tab has its core lane and hover and flight as parallel lanes");
        _game.Menu.Close();
    }

    /// <summary>The visor's labels at 1080p and 1440p, measured from their boxes: none on another, none off the glass.</summary>
    void HudBoxChecks()
    {
        foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(2560, 1440) })
        {
            HudLayout l = HudLayout.Compute(size, 1f);
            string problem = l.Problem(size);
            var cells = Alerts.RowCells(l.Alerts, l.Scale, Alerts.MostHazards);
            bool apart = cells.Select((c, i) => cells.Skip(i + 1).All(d => !c.Sign.Intersects(d.Sign) && !c.Timer.Intersects(d.Timer))).All(x => x);
            bool onLine = cells.All(c => MathF.Abs(c.Sign.Position.Y - l.Toolbar.Position.Y) < 0.5f && c.Sign.Position.X < l.Toolbar.Position.X);
            Check(problem == null && cells.Count == Alerts.MostHazards && apart && onLine, $"HUD at {size.X}x{size.Y}: {l.Boxes.Count()} label boxes, none overlapping; room for {cells.Count} of {Alerts.MostHazards} alerts on the toolbar's line at the far left, none on another{(problem == null ? "" : ": " + problem)}");
        }
    }

    /// <summary>
    /// The alerts' row (the visor's, which stays put over menus) against a menu panel on the page it is showing, at 1080p
    /// and 1440p: no line of text, widget or panel of the menu on it.
    /// </summary>
    static List<string> RowProblems(MenuPanel panel)
    {
        var problems = new List<string>();
        var design = new Vector2(Gui.Width, Gui.Height);
        foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(2560, 1440) })
        {
            float k = MathF.Min(size.X / design.X, size.Y / design.Y);
            Vector2 offset = (size - design * k) * 0.5f;
            Rect2 Px(Rect2 r) => new(offset + r.Position * k, r.Size * k);
            Rect2 box = HudLayout.Compute(size, 1f).Alerts;
            string at = $"{size.X:0}x{size.Y:0}";
            foreach (var (text, rect, _) in panel.Gui.Texts)
                if (Px(rect).Grow(-1f).Intersects(box)) problems.Add($"{at}: \"{text}\" is on the alerts' row");
            foreach (var (id, rect) in panel.Gui.Rects)
                if (Px(rect).Intersects(box)) problems.Add($"{at}: widget {id} is on the alerts' row");
            foreach (Rect2 fill in panel.Gui.Fills)
                if (!Px(fill).Encloses(box) && Px(fill).Intersects(box)) problems.Add($"{at}: a panel {fill} is on the alerts' row");
        }
        return problems;
    }

    /// <summary>The escape menu's pages and its quit and new game questions: the alerts' row clear of every panel.</summary>
    IEnumerable<int> EscapeRowChecks()
    {
        Gui.Measure = true;
        EscapeMenu esc = _game.Escape;
        var pages = new List<(string name, Action set)> { ("the world", () => { esc.Root.Page = EscapePage.World; esc.Root.Confirming = false; }) };
        foreach (OptionsTab tab in Enum.GetValues<OptionsTab>())
            pages.Add(($"options: {tab}", () => { esc.Root.Page = EscapePage.Options; esc.Root.Options.Tab = tab; esc.Root.Confirming = false; }));
        pages.Add(("the quit question", () => { esc.Root.Page = EscapePage.World; esc.Root.Confirming = true; }));
        pages.Add(("the new game question", () => { esc.Root.Page = EscapePage.World; esc.Root.Asking = EscapeQuestion.NewGame; }));
        esc.Open();
        foreach (var (name, set) in pages)
        {
            set();
            foreach (int x in _drive.Wait(3)) yield return x;
            NoteLines($"escape menu, {name}", esc.Root);
            List<string> problems = RowProblems(esc.Root);
            problems.AddRange(SlotProblems(esc.Root));
            Check(problems.Count == 0 && esc.Root.Gui.Texts.Count > 0, $"the escape menu, {name}: the alerts' row is clear of every panel at 1080p and 1440p, and the notices' corner of its own text and widgets{(problems.Count == 0 ? "" : ": " + string.Join("; ", problems.Take(6)))}");
        }
        esc.Root.Confirming = false;
        esc.Root.Page = EscapePage.World;
        esc.Close();
        Gui.Measure = false;
        _game.Menu.Open(MenuTab.You);
        foreach (int x in _drive.Wait(2)) yield return x;
        Gui.Measure = true;
        _game.Menu.Root.Tab = MenuTab.Stats;
        foreach (int x in _drive.Wait(3)) yield return x;
        Gui.Measure = false;
        var title = _game.Menu.Root.Gui.Texts.FirstOrDefault(t => t.Text == "SUIT TERMINAL");
        float firstTab = _game.Menu.Root.Gui.Rects.Where(r => r.Key.StartsWith("tab:")).Select(r => r.Value.Position.X).DefaultIfEmpty(0f).Min();
        Check(title.Text != null && title.Box.End.X + 8f <= firstTab, $"the header's title ends ({title.Box.End.X:F0}) before the first tab ({firstTab:F0}) that now starts further left");
        _game.Menu.Close();
        foreach (int x in _drive.Wait(2)) yield return x;
    }

    /// <summary>
    /// Every weather kind and every other kind of alert: it warns by a sign of its own (in the sign set, never the generic "!"),
    /// or it never warns. Clear skies, clouds and fog are no danger on foot and never do; an alert with no sign of its own is not shown.
    /// </summary>
    void AlertSignChecks()
    {
        var sim = _game.Zone.Weather.Sim;
        Vector3 feet = _game.Survivor.WorldFeet;
        var problems = new List<string>();
        int warns = 0, silent = 0;
        foreach (WeatherKind kind in Enum.GetValues<WeatherKind>())
        {
            bool severe = _game.Zone.Data.Weather.State(kind).Severe;
            string sign = WeatherSim.SignOf(kind);
            sim.Snap(kind);
            // Where this weather can be itself: the region rules keep a dust storm off wet ground and a
            // tornado storm off broken land, so look from the nearest ground they allow it (the check is of
            // the warning, not the region rules).
            Vector3 at = feet;
            for (int k = 0; k < 600 && sim.LocalKind(kind, at) != kind; k++)
            {
                float a = k * 2.399f, r = 150f + k * 12f;
                at = feet + new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
            }
            if (sim.LocalKind(kind, at) != kind)
            {
                GD.Print($"      ({kind}: no ground on this world within 7 km lets it be itself: a {sim.LocalKind(kind, feet)} here, warned as that)");
                at = feet;
            }
            var list = new List<WarningEntry>();
            sim.GetWarnings(list, at);
            var own = list.Where(w => w.Kind == kind).ToList();
            bool possible = sim.LocalKind(kind, at) == kind;
            if (severe && !possible) silent++;
            else if (severe)
            {
                if (!Alerts.Specific(sign)) problems.Add($"{kind} is severe but its sign '{sign ?? "none"}' is not a sign of its own");
                else if (own.Count != 1 || own[0].Sign != sign) problems.Add($"{kind} should raise one '{sign}' warning, raised {own.Count}");
                else warns++;
            }
            else if (own.Count > 0) problems.Add($"{kind} is no hazard but warns ('{own[0].Sign ?? "none"}')");
            else silent++;
            foreach (WarningEntry w in list)
                if (w.Level != WarningLevel.Advisory && !Alerts.Specific(w.Sign)) problems.Add($"{kind}: a warning with the sign '{w.Sign ?? "none"}'");
        }
        sim.Snap(WeatherKind.Clear);
        sim.ClearTornadoes();
        sim.ClearHazards();
        sim.ClearCells();
        Check(problems.Count == 0 && warns > 0, $"every weather kind warns by a sign of its own or not at all: {warns} warn, {silent} never do{(problems.Count == 0 ? "" : ": " + string.Join("; ", problems))}");
        Check(WeatherSim.SignOf(WeatherKind.Clear) == null && WeatherSim.SignOf(WeatherKind.Clouds) == null && WeatherSim.SignOf(WeatherKind.Fog) == null,
            "clear skies, clouds and fog name no sign: they cannot raise an alert");

        // The strikes, the funnel, lava and every attacker.
        var strikes = new List<WarningEntry>();
        sim.StartStrike(HazardKind.Lightning, feet + new Vector3(20f, 0f, 0f));
        sim.StartStrike(HazardKind.Debris, feet + new Vector3(10f, 0f, 0f));
        sim.SpawnTornado(feet + new Vector3(0f, 0f, 220f), feet);
        sim.GetWarnings(strikes, feet);
        sim.ClearTornadoes();
        sim.ClearHazards();
        sim.ClearCells();
        var badStrike = strikes.Where(w => !Alerts.Specific(w.Sign)).Select(w => w.Sign ?? "none").ToList();
        Check(strikes.Count >= 3 && badStrike.Count == 0, $"lightning, falling debris and a tornado each warn by their own sign ({string.Join(", ", strikes.Select(w => w.Sign))}){(badStrike.Count == 0 ? "" : $", but {string.Join(", ", badStrike)} is not one")}");
        var badAttackers = _game.Zone.Wildlife.Fauna.Species.Where(s => !Alerts.Specific(Alerts.SignFor(s))).Select(s => s.CommonName).ToList();
        string[] others = { "storm", "lava", "husk_hazard", "hostile", "wildlife" };
        Check(badAttackers.Count == 0 && others.All(Alerts.Specific), $"every attacker of this world and the lightning strike and lava alerts have a sign of their own{(badAttackers.Count == 0 ? "" : $" (not {string.Join(", ", badAttackers)})")}");
        Check(!Alerts.Specific("hazard") && !Alerts.Specific(null) && !Alerts.Specific("no-such-sign"), "the generic hazard sign, no sign and a sign that is not there are not signs of their own");
        _game.Alerts.Attack("hazard", feet);
        _game.Alerts.Attack("no-such-sign", feet);
        Check(!_game.Alerts.Attacks.Any(), "and an alert asked for with one of those is skipped, not shown");
    }

    /// <summary>Every page of the Tab menu: no two lines of text on top of each other, and none cut short by its box.</summary>
    IEnumerable<int> MenuTextChecks()
    {
        Gui.Measure = true;
        var pages = new List<(string name, Action open)>
        {
            ("you: fabricator", () => { _game.Menu.Open(MenuTab.You); _game.Menu.Root.You.ShowBlocks = false; }),
            ("you: blocks", () => { _game.Menu.Open(MenuTab.You); _game.Menu.Root.You.ShowBlocks = true; }),
            ("stats", () => _game.Menu.Open(MenuTab.Stats)),
            ("core", () => _game.Menu.Open(MenuTab.Core)),
        };
        foreach (string tab in Slice.Items.Progress.Tabs) pages.Add(($"tech: {tab}", () => { _game.Menu.Open(MenuTab.Tech); _game.Menu.Root.Tech.Tab = tab; }));
        foreach (MapLevel level in Enum.GetValues<MapLevel>()) pages.Add(($"map: {level}", () => _game.Menu.OpenMap(level)));
        foreach (var entry in _game.Menu.Root.Blueprints.Library.Starters)
            pages.Add(($"blueprints: {entry.Name}", () => { _game.Menu.Open(MenuTab.Blueprints); _game.Menu.Root.Blueprints.Select(entry); }));
        foreach (var (name, open) in pages)
        {
            open();
            foreach (int x in _drive.Wait(3)) yield return x;
            NoteLines($"menu, {name}", _game.Menu.Root);
            List<string> problems = _game.Menu.Root.Gui.TextProblems();
            List<string> strip = RowProblems(_game.Menu.Root);
            strip.AddRange(SlotProblems(_game.Menu.Root));
            Check(problems.Count == 0 && strip.Count == 0 && _game.Menu.Root.Gui.Texts.Count > 0,
                $"the menu's {name} page: {_game.Menu.Root.Gui.Texts.Count} lines, none overlapping or cut short, and the alerts' row and the notices' corner clear of every panel at 1080p and 1440p{(problems.Count + strip.Count == 0 ? "" : ": " + string.Join("; ", problems.Concat(strip).Take(6)))}");
        }
        _game.Menu.Root.You.ShowBlocks = false;
        _game.Menu.Close();
        Gui.Measure = false;
    }

    /// <summary>At the start, before anything else is found or pinged, the compass holds the bunker and the place in view from its way out, and nothing else.</summary>
    void CompassAtStart()
    {
        var marks = _game.Visor.Markers();
        var kinds = marks.Select(m => m.Kind).ToList();
        var seen = _game.Bunker?.Poi;
        bool landmark = seen != null && _game.Bunker.Landmark != null;
        string[] want = landmark ? new[] { "bunker", "place" } : new[] { "bunker" };
        Check(kinds.SequenceEqual(want) && (!landmark || marks[1].Tag == seen.Name.ToUpperInvariant()),
            $"at the start the markers show the bunker{(landmark ? $" and {seen.Name}, the place in view from its way out" : "")} and nothing else ({string.Join(", ", marks.Select(m => $"{m.Kind} {m.Tag}"))})");
        if (landmark)
            Check(_game.Spaceflight.Charts.Has(_game.Spaceflight.StartPlaceKey ?? "") && _game.Spaceflight.Charts.OfKind("place").Count() == 1,
                $"it is found from the first moment, on the map too, and it is the only place found ({_game.Spaceflight.Charts.OfKind("place").Count()})");
    }

    /// <summary>The handheld scanner: free in the suit fabricator; a click pings what is far off onto the compass for ten seconds, fading, and nothing stays.</summary>
    IEnumerable<int> ScannerChecks()
    {
        ItemDef scanner = Items.Item("scanner");
        Check(scanner.Recipe != null && scanner.Recipe.Inputs.Count == 0 && _game.Fabricator.Knows(scanner) && _game.Progress.ItemAllowed(scanner), "the handheld scanner is free in the suit fabricator from the start");
        foreach (int x in _play.OpenMenu(MenuTab.You)) yield return x;
        foreach (int x in _play.Order("fab", "scanner", 1)) yield return x;
        foreach (int x in _play.WaitFor(() => Pack["scanner"] > 0, 15)) yield return x;
        foreach (int x in _play.ToToolbar("scanner", 8)) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
        foreach (int x in _play.Select(8)) yield return x;
        Check(Pack.Hand == HandKind.Scanner, "made, on the toolbar and in hand");
        int charted = _game.Spaceflight.Charts.OfKind("deposit").Count();
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        int pinged = _game.Visor.Markers().Count(m => m.Kind == "ping");
        double at = _game.Stats.Seconds;
        Check(pinged > 0 && _game.Pings.Count == pinged, $"a click pings {pinged} things onto the compass ({string.Join(", ", _game.Pings.Select(p => p.Label ?? "?"))})");
        foreach (int x in _play.WaitFor(() => _game.Stats.Seconds - at > 8.5, 12)) yield return x;
        var late = _game.Visor.Markers().Where(m => m.Kind == "ping").ToList();
        Check(late.Count == pinged && late.All(m => m.Alpha < 1f && m.Alpha > 0f), $"after 8.5 s they are still marked, fading (alpha {(late.Count > 0 ? late[0].Alpha : 0):F2})");
        foreach (int x in _play.WaitFor(() => _game.Stats.Seconds - at > 10.2, 5)) yield return x;
        Check(_game.Visor.Markers().All(m => m.Kind != "ping"), "after ten seconds the pings are gone");
        Check(_game.Spaceflight.Charts.OfKind("deposit").Count() == charted, "and nothing pinged stays charted: only going near a deposit marks it");
    }

    /// <summary>A tornado forming nearby: its sign on the left of the view with a timer, an arrow to it, and no sentence anywhere.</summary>
    IEnumerable<int> AlertChecks()
    {
        var sim = _game.Zone.Weather.Sim;
        string said = "";
        void Hear(string t, Color c) => said += t + "|";
        _game.Said += Hear;
        Vector3 feet = _game.Survivor.WorldFeet;
        float angle = (float)new Random().NextDouble() * Mathf.Tau;
        sim.SpawnTornado(feet + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 220f, feet);
        foreach (int x in _drive.Wait(6)) yield return x;
        Alerts.Shown? t = _game.Alerts.Hazards.FirstOrDefault(h => h.Sign == "tornado");
        Check(t != null && t.Value.Seconds > 0 && t.Value.From != null && Alerts.HasSign("tornado"),
            $"a tornado forming raises its sign with a timer ({(t == null ? "none" : Alerts.Clock(t.Value.Seconds))}) and a bearing");
        Check(said == "", $"and nothing is said about it{(said == "" ? "" : $": \"{said}\"")}");
        Check(typeof(Alerts.Shown).GetProperties().All(p => p.Name != "Text"), "an alert carries no text at all: a sign, a timer and a direction");
        Check(_game.Alerts.Layer > _game.Escape.Layer && _game.Alerts.Layer > _game.Menu.Layer && _game.Alerts.ProcessMode == Node.ProcessModeEnum.Always,
            "alerts draw over the Tab menu and the escape menu, and run while it is up");
        _game.Menu.Open(MenuTab.You);
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(_game.Alerts.Hazards.Any(h => h.Sign == "tornado"), "with the Tab menu open the tornado's sign is still up");
        _game.Menu.Close();
        sim.ClearTornadoes();
        sim.ClearHazards();
        sim.ClearCells();
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(_game.Alerts.Hazards.All(h => h.Sign != "tornado"), "and goes once the tornado has");
        _game.Said -= Hear;
    }
}
