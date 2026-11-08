using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
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
