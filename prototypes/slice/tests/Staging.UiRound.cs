using System.Collections.Generic;
using System.Linq;
using Godot;
using Slice.Game;
using Slice.Ui;
using Slice.Vessels;
using GalHack.Items;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The UI round's proof, one run, daylight: <c>--view=uiround --dir=folder</c> writes ui-hud-alert (a
/// tornado forming and a beast's attack), ui-compass (out of the bunker, nothing found yet),
/// ui-scanner (a ping), ui-tech-TAB for every tech tab, ui-fabricator, ui-inventory (a container open
/// beside the bag), ui-core (a survival constructor's page) and ui-galaxy. Anything newer than the
/// game it runs in is reached by name, so the same view shoots the game from before the round.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> UiRoundShots()
    {
        string dir = _args.GetValueOrDefault("dir", "user://");
        _game.Zone.Sky.TimeOfDay = Num("time", 0.36);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        Hand(("iron-ore", 160), ("copper-ore", 80), ("carbon-ore", 70), ("silicon-ore", 30), ("biomass", 14), ("stone", 22), ("ice", 12), ("meat", 6), ("hide", 2));
        _game.Progress.Earn(900, "staging");
        IEnumerable<int> Frame(string name)
        {
            _game.Controls.Mouse = new Vector2(-100, -100);
            foreach (int x in _drive.Wait(6)) yield return x;
            foreach (int x in Shot($"{dir}/ui-{name}.png")) yield return x;
        }
        object Call(object on, string method, params object[] args) => on?.GetType().GetMethod(method)?.Invoke(on, args);
        object Get(object on, string prop) => on?.GetType().GetProperty(prop)?.GetValue(on);

        // A survival constructor set down outside, working (the core page's machine).
        foreach (int x in Play.OpenMenu(MenuTab.You)) yield return x;
        foreach (int x in Play.Order("fab", "survival-constructor", 1)) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
        _game.CraftTimeScale = 200;
        foreach (int x in Play.WaitFor(() => _game.Pack["survival-constructor"] > 0, 20)) yield return x;
        _game.CraftTimeScale = 1;
        foreach (int x in Play.ToToolbar("survival-constructor", 2)) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
        Vector3 away = _game.Bunker.Ahead(5f);
        foreach (int x in _drive.WalkTo(away, 1f, 20f)) yield return x;
        Vector3 spot = _game.Zone.GroundAt(_game.Bunker.Ahead(9f, 2f).X, _game.Bunker.Ahead(9f, 2f).Z);
        foreach (int x in Play.PlaceOnGround(2, spot)) yield return x;
        Device fab = _game.Zone.Devices.FirstOrDefault();
        if (fab != null)
        {
            fab.Storage.AddAll(new Bill().Add("iron-ore", 60).Add("carbon-ore", 40).Add("copper-ore", 30));
            fab.Constructor.Order("big-core", 1);
            fab.Tick(3);
        }

        foreach (int x in _drive.Wait(260)) yield return x; // the level-up banner from the staging's experience has gone
        // Out on the surface, looking along the ground: the compass with what is found so far.
        Vector3 eye = _game.Survivor.WorldEye();
        Vector3 look = _game.Survivor.WorldLook();
        foreach (int x in _drive.LookAt(eye + new Vector3(look.X, 0f, look.Z).Normalized() * 40f + Vector3.Down * 1.5f, 12)) yield return x;
        foreach (int x in Frame("compass")) yield return x;

        // The scanner's ping (a game from before the round has no scanner: the frame shows the same compass).
        if (_game.Zone.Data.Items.TryGet("scanner", out _)) _game.Pack.Add("scanner", 1);
        Call(_game, "Scan");
        foreach (int x in Frame("scanner")) yield return x;

        // A tornado forming off to one side, and a beast's bite from the other.
        Vector3 feet = _game.Survivor.WorldFeet;
        Vector3 side = new Vector3(look.Z, 0f, -look.X).Normalized();
        _game.Zone.Weather.Sim.SpawnTornado(feet + side * 260f + new Vector3(look.X, 0, look.Z).Normalized() * 120f, feet);
        foreach (int x in _drive.Wait(30)) yield return x;
        Call(Get(_game, "Alerts"), "Attack", "wildlife", feet - side * 6f);
        Call(_game.Visor, "Hurt", feet - side * 6f);
        foreach (int x in Frame("hud-alert")) yield return x;
        _game.Zone.Weather.Sim.ClearTornadoes();

        // The Tab menu: the fabricator and bag, a container beside the bag, the core page, every tech tab, the galaxy.
        foreach (int x in Play.OpenMenu(MenuTab.You)) yield return x;
        foreach (int x in Play.Order("fab", "trail-ration", 5)) yield return x;
        foreach (int x in Frame("fabricator")) yield return x;
        if (fab != null)
        {
            _game.Open(new OpenContainer { Title = $"{fab.Label} hopper", Grid = fab.Grid, Device = fab, Where = () => fab.GlobalPosition });
            foreach (int x in Frame("inventory")) yield return x;
            _game.CloseContainer();
        }
        foreach (int x in Play.OpenMenu(MenuTab.Core)) yield return x;
        foreach (int x in Frame("core")) yield return x;
        foreach (int x in Play.OpenMenu(MenuTab.Tech)) yield return x;
        foreach (string tab in Slice.Items.Progress.Tabs)
        {
            _game.Menu.Root.Tech.Tab = tab;
            foreach (int x in _drive.Wait(60)) yield return x; // the tiles' icons render a few a frame
            foreach (int x in Frame($"tech-{tab}")) yield return x;
        }
        _game.Menu.OpenMap(MapLevel.Galaxy);
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (int x in Frame("galaxy")) yield return x;
        _game.Menu.Close();
    }
}
