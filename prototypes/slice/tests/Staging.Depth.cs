using System.Collections.Generic;
using System.Linq;
using GalHack.Stars;
using Godot;
using Slice.Ui;

namespace Slice.Tests;

/// <summary>
/// Galactic depth's proof: the new-game screen with each preset and a custom set of options, and the
/// galaxy map showing a star's depth and band (your own, then one near the core).
/// </summary>
public sealed partial class Staging
{
    /// <summary>The new-game screen as a new game shows it, clicked through its own widgets: DEFAULT, CORE-WARD, a custom set, RANDOM.</summary>
    IEnumerable<int> NewGameShots()
    {
        var screen = new StartScreen { Name = "Start" };
        _game.AddChild(screen);
        for (int i = 0; i < 20; i++) yield return 0;
        StartRoot root = screen.Root;
        IEnumerable<int> Shoot(string tag)
        {
            for (int i = 0; i < 6; i++) yield return 0;
            var (world, mode) = root.Choice();
            GD.Print($"(new game {tag}: preset {root.CurrentPreset ?? "custom"}; would start {world}, {mode})");
            Snap(tag);
        }
        foreach (int x in Shoot("a-default")) yield return x;
        root.Click("start:preset-mid");
        foreach (int x in Shoot("b-mid")) yield return x;
        root.Click("start:preset-core");
        foreach (int x in Shoot("c-coreward")) yield return x;
        // Custom: each option stepped through its own arrows.
        void Steps(string id, int n)
        {
            for (int i = 0; i < n; i++) root.Click($"start:{id}");
        }
        Steps("depth-prev", 2);
        Steps("type-next", 3);
        Steps("theme-next", 1 + (int)Num("theme", 5));
        Steps("fall-next", 1 + (int)Num("fall", 7));
        Steps("mid-next", 1);
        Steps("old-next", 3);
        Steps("now-next", 1);
        Steps("size-next", 3);
        Steps("mode-next", 1);
        root.Click("start:seed");
        foreach (char c in "red moon") root.Typed(Key.None, c);
        root.Typing = false;
        foreach (int x in Shoot("d-custom")) yield return x;
        root.Click("start:preset-random");
        foreach (int x in Shoot("e-random")) yield return x;
        screen.QueueFree();
        yield return 0;
    }

    /// <summary>The galaxy map in daylight: your star's depth and band, then a star near the core picked.</summary>
    IEnumerable<int> GalaxyDepth()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Sky.Paused = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        Stars.Finish();
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _drive.Tap(Key.Tab)) yield return x;
        _game.Menu.OpenMap(MapLevel.Galaxy);
        MapLevels levels = _game.Menu.Root.Map.Levels;
        for (int i = 0; i < 90; i++) yield return 0;
        Galaxy g = Stars.Galaxy;
        GD.Print($"(depth: you are at {g.Name(Stars.Here)}, depth {g.DepthOf(Stars.Here):0.00} {Galaxy.Band(g.DepthOf(Stars.Here))}; the world rolled {_game.Zone.Planet.Roll?.One(PoiGenerator.Gen.LayerKind.Zone)?.Id})");
        Snap("a-here");
        foreach (int x in _drive.ClickWidget("map:whole")) yield return x;
        int core = Enumerable.Range(0, g.Stars.Length).Where(id => g.DepthOf(id) is > 0.82f and < 0.9f && g.Stars[id].Region != Region.Satellite).OrderBy(id => g.Stars[id].Light).LastOrDefault();
        _game.Spaceflight.Charts.ChosenStar = core;
        levels.Trail.Star = core;
        for (int i = 0; i < 40; i++) yield return 0;
        GD.Print($"(depth: picked {g.Name(core)}, depth {g.DepthOf(core):0.00} {Galaxy.Band(g.DepthOf(core))})");
        Snap("b-core-star");
    }
}
