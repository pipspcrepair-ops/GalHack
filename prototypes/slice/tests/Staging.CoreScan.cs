using System.Collections.Generic;
using Godot;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>The hand scanner in hand outside a place with a core: <c>--view=corescan --out=FILE.png</c>, the x-ray marker on a core behind its walls.</summary>
public sealed partial class Staging
{
    IEnumerable<int> CoreScanShot()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.36);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Pack.Add("scanner", 1);
        foreach (int x in CoreScanStage.Hold(Play, "scanner", _ => { })) yield return x;
        var stage = new CoreScanStage();
        foreach (int x in stage.Go(_game, _drive)) yield return x;
        GD.Print($"corescan: {(stage.Place == null ? "no place with a blocked view of its core" : stage.Place.Site.Name)}");
        _game.Controls.Mouse = new Vector2(-100, -100);
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (var p in _game.Visor.LayoutMarkers(_game.GetViewport().GetVisibleRect().Size, _game.Visor.UiScale))
            GD.Print($"marker {p.Mark.Kind} at {p.At} {(p.Pinned ? "pinned" : "")} {Slice.Ui.Visor.Distance(p.Distance)}");
        foreach (int x in Shot(_args.GetValueOrDefault("out", "user://corescan.png"))) yield return x;
    }
}
