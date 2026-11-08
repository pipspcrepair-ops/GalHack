using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using Godot;
using Slice.Items;
using Slice.Ui;

namespace Slice.Tests;

/// <summary>
/// The UI round: hazards as symbols with a timer and no words, a compass that starts with only the
/// bunker, the scanner's ping marking it for ten seconds, every tech tab on one 1080p screen, and
/// no two HUD labels on top of each other.
/// </summary>
public partial class BuildTest
{
    IEnumerable<int> UiRound()
    {
        GD.Print("-- the UI round --");
        foreach (int x in AlertChecks()) yield return x;
        foreach (int x in ScannerChecks()) yield return x;
        foreach (int x in MenuTextChecks()) yield return x;
        HudBoxChecks();
        foreach (int x in TechFitChecks()) yield return x;
        foreach (int x in GalaxyNameChecks()) yield return x;
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
            Check(problem == null && l.Alerts.Size.Y >= HudLayout.AlertIcon * l.Scale * 2, $"HUD at {size.X}x{size.Y}: {l.Boxes.Count()} label boxes, none overlapping, room for two alerts{(problem == null ? "" : ": " + problem)}");
        }
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
        foreach (var (name, open) in pages)
        {
            open();
            foreach (int x in _drive.Wait(3)) yield return x;
            List<string> problems = _game.Menu.Root.Gui.TextProblems();
            Check(problems.Count == 0 && _game.Menu.Root.Gui.Texts.Count > 0, $"the menu's {name} page: {_game.Menu.Root.Gui.Texts.Count} lines, none overlapping or cut short{(problems.Count == 0 ? "" : ": " + string.Join("; ", problems.Take(6)))}");
        }
        _game.Menu.Root.You.ShowBlocks = false;
        _game.Menu.Close();
        Gui.Measure = false;
    }

    /// <summary>At the start, before anything is found or pinged, the compass holds the bunker and nothing else.</summary>
    void CompassAtStart()
    {
        var kinds = _game.Visor.CompassMarks().Select(m => m.Kind).ToList();
        Check(kinds.SequenceEqual(new[] { "bunker" }), $"at the start the compass shows only the bunker ({string.Join(", ", kinds)})");
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
        int pinged = _game.Visor.CompassMarks().Count(m => m.Kind == "ping");
        double at = _game.Stats.Seconds;
        Check(pinged > 0 && _game.Pings.Count == pinged, $"a click pings {pinged} things onto the compass ({string.Join(", ", _game.Pings.Select(p => p.Label ?? "?"))})");
        foreach (int x in _play.WaitFor(() => _game.Stats.Seconds - at > 8.5, 12)) yield return x;
        var late = _game.Visor.CompassMarks().Where(m => m.Kind == "ping").ToList();
        Check(late.Count == pinged && late.All(m => m.Alpha < 1f && m.Alpha > 0f), $"after 8.5 s they are still marked, fading (alpha {(late.Count > 0 ? late[0].Alpha : 0):F2})");
        foreach (int x in _play.WaitFor(() => _game.Stats.Seconds - at > 10.2, 5)) yield return x;
        Check(_game.Visor.CompassMarks().All(m => m.Kind != "ping"), "after ten seconds the pings are gone");
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
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(_game.Alerts.Hazards.All(h => h.Sign != "tornado"), "and goes once the tornado has");
        _game.Said -= Hear;
    }
}
