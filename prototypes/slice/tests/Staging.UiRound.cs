using System.Collections.Generic;
using System.Linq;
using Godot;
using Slice.Game;
using Slice.Items;
using Slice.Ui;
using Slice.Vessels;
using GalHack.Items;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The UI round's proof, one run, daylight: <c>--view=uiround --dir=folder</c> writes ui-hud-alert (a
/// tornado forming, lightning building and a beast's attack), ui-compass (out of the bunker: the bunker and the place in view, nothing else found yet),
/// ui-menu-alert, ui-menu-alert-stats, ui-menu-alert-blueprints and ui-esc-alert (the same alerts over the Tab menu's pages and
/// the escape menu), ui-scanner (a ping), ui-tech-TAB for every tech tab, ui-fabricator, ui-inventory (a container open
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
        _game.Progress.Earn(900, "staging", false);
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
            fab.Constructor.Order("core", 1);
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

        // A tornado forming off to one side, lightning building far off on the other, and a beast's bite.
        Vector3 feet = _game.Survivor.WorldFeet;
        Vector3 side = new Vector3(look.Z, 0f, -look.X).Normalized(), ahead = new Vector3(look.X, 0, look.Z).Normalized();
        var sim = _game.Zone.Weather.Sim;
        sim.SpawnTornado(feet + side * 260f + ahead * 120f, feet);
        // A strike's charge builds for a few seconds only: a fresh one before each frame, landing 170 m off.
        void Threats()
        {
            sim.StartStrike(Weather.Model.HazardKind.Lightning, feet - side * 150f + ahead * 80f);
            Call(Get(_game, "Alerts"), "Attack", "wildlife", feet - side * 6f);
        }
        foreach (int x in _drive.Wait(30)) yield return x;
        Threats();
        Call(_game.Visor, "Hurt", feet - side * 6f);
        foreach (int x in Frame("hud-alert")) yield return x;
        // With the Tab menu open the same alerts stay on the toolbar's line at the far left, clear of the paperdoll and the slots.
        foreach (int x in Play.OpenMenu(MenuTab.You)) yield return x;
        Threats();
        foreach (int x in Frame("menu-alert")) yield return x;
        // The pages that reach lowest at the left (the stats' last rows, the blueprints' SAVE) stay clear of them too.
        foreach (MenuTab tab in new[] { MenuTab.Stats, MenuTab.Blueprints })
        {
            foreach (int x in Play.OpenMenu(tab)) yield return x;
            Threats();
            foreach (int x in Frame($"menu-alert-{tab.ToString().ToLowerInvariant()}")) yield return x;
        }
        foreach (int x in Play.CloseMenu()) yield return x;
        // And over the escape menu, the world stopped behind it.
        foreach (int x in _drive.Tap(Key.Escape)) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Threats();
        foreach (int x in Frame("esc-alert")) yield return x;
        foreach (int x in _drive.Tap(Key.Escape)) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        _game.Escape.Close();
        sim.ClearTornadoes();
        sim.ClearHazards();

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

    /// <summary>
    /// Recipes' inputs as icons and counts: <c>--view=fabicons --dir=folder</c> writes fabicons-you (the You page's
    /// fabricator, some inputs short), fabicons-short (pointing at a short input of a recipe the bag can't pay for),
    /// fabicons-blocks (the blocks list), fabicons-screen (a placed survival constructor's own screen) and
    /// fabicons-core (the same constructor on the core page).
    /// </summary>
    IEnumerable<int> FabIconShots()
    {
        string dir = _args.GetValueOrDefault("dir", "user://");
        _game.Zone.Sky.TimeOfDay = Num("time", 0.36);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        Hand(("iron-ore", 60), ("copper-ore", 30), ("carbon-ore", 20), ("silicon-ore", 6), ("stone", 8), ("ice", 2));
        _game.Progress.Earn(900, "staging", false);
        IEnumerable<int> Frame(string name, Vector2? mouse = null)
        {
            _game.Controls.Mouse = mouse ?? new Vector2(-100, -100);
            foreach (int x in _drive.Wait(8)) yield return x;
            foreach (int x in Shot($"{dir}/fabicons-{name}.png")) yield return x;
        }

        // A survival constructor made and set down outside, with some ore in its hopper and an order on.
        foreach (int x in Play.OpenMenu(MenuTab.You)) yield return x;
        foreach (int x in Play.Order("fab", "survival-constructor", 1)) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
        _game.CraftTimeScale = 200;
        foreach (int x in Play.WaitFor(() => _game.Pack["survival-constructor"] > 0, 20)) yield return x;
        _game.CraftTimeScale = 1;
        foreach (int x in Play.ToToolbar("survival-constructor", 2)) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
        foreach (int x in _drive.WalkTo(_game.Bunker.Ahead(5f), 1f, 20f)) yield return x;
        Vector3 spot = _game.Zone.GroundAt(_game.Bunker.Ahead(9f, 2f).X, _game.Bunker.Ahead(9f, 2f).Z);
        foreach (int x in Play.PlaceOnGround(2, spot)) yield return x;
        Device fab = _game.Zone.Devices.FirstOrDefault();
        if (fab != null)
        {
            fab.Storage.AddAll(new Bill().Add("iron-ore", 24).Add("carbon-ore", 10).Add("copper-ore", 6));
            fab.Constructor.Order("hull-plate", 2);
            fab.Tick(1);
        }

        // Its own screen, from in front of it.
        _game.MachineScreens?.Sync();
        foreach (int x in _drive.Wait(30)) yield return x;
        var mounted = fab == null ? null : _game.MachineScreens?.Screens.Values.FirstOrDefault(m => m.Holder.GetParent() == fab);
        if (mounted != null)
        {
            Vector3 face = mounted.Holder.GlobalPosition;
            Vector3 front = -fab.GlobalBasis.Z;
            front = new Vector3(front.X, 0f, front.Z).Normalized();
            foreach (int x in StandAndLook(new Vector3(face.X, 0f, face.Z) + front * 1.3f, face, 20)) yield return x;
            GD.Print($"(the constructor at {fab.GlobalPosition}, its screen at {face}, facing {front}; standing at {_game.Survivor.WorldFeet})");
            foreach (int x in _drive.Wait(150)) yield return x; // its icons render a few a frame, and it redraws as they come
            foreach (int x in Frame("screen")) yield return x;
        }

        // The You page's fabricator, every recipe: what the bag covers in plain, what it doesn't in amber (the fabricator makes it) or red.
        foreach (int x in Play.OpenMenu(MenuTab.You)) yield return x;
        _game.Menu.Root.You.ShowBlocks = false;
        ItemViews.Get("recipes:fab").Show = ItemGroup.None;
        foreach (int x in _drive.Wait(120)) yield return x;
        foreach (int x in Frame("you")) yield return x;

        // A recipe the bag can't pay for, pointing at its first short input (the name and stock on hover).
        Gui.Measure = true;
        foreach (int x in _drive.Wait(3)) yield return x;
        Gui.Measure = false;
        var texts = _game.Menu.Root.Gui.Texts;
        Vector2? at = null;
        foreach (var name in texts)
        {
            ItemDef made = _game.Zone.Data.Items.Items.FirstOrDefault(i => i.Recipe != null && _game.Fabricator.Knows(i) && name.Text.StartsWith(i.Name, System.StringComparison.Ordinal));
            int k = made == null ? -1 : made.Recipe.Inputs.ToList().FindIndex(i => _game.Pack[i.Item.Id] < i.Count);
            if (k < 0) continue;
            float y = name.Box.GetCenter().Y;
            var counts = texts.Where(t => t.Box.Position.X > name.Box.End.X && System.Math.Abs(t.Box.GetCenter().Y - y) < 3f && t.Text.All(char.IsDigit))
                .OrderBy(t => t.Box.Position.X).ToList();
            if (k >= counts.Count) continue;
            at = new Vector2(counts[k].Box.Position.X - 16f, y);
            GD.Print($"(pointing at {made.Recipe.Inputs[k].Item.Name}, {made.Recipe.Inputs[k].Count} of it for {made.Name})");
            break;
        }
        foreach (int x in Frame("short", at)) yield return x;

        // The blocks list: each block's inputs the same way.
        _game.Menu.Root.You.ShowBlocks = true;
        foreach (int x in _drive.Wait(120)) yield return x;
        foreach (int x in Frame("blocks")) yield return x;
        _game.Menu.Root.You.ShowBlocks = false;

        // The placed constructor on the core page.
        foreach (int x in Play.OpenMenu(MenuTab.Core)) yield return x;
        foreach (int x in _drive.Wait(60)) yield return x;
        foreach (int x in Frame("core")) yield return x;
        _game.Menu.Close();
    }
}
