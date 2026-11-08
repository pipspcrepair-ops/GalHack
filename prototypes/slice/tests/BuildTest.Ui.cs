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
