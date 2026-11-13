using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Ui;
using Slice.Vessels;
using Structure = GalHack.Structures.Structure;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

public sealed partial class Staging
{
    /// <summary>The CORE tab's light styling panel, on a base with lights: --panel=FILE.png.</summary>
    IEnumerable<int> LightPanelView()
    {
        string file = _args.TryGetValue("panel", out string o) ? o : "user://lightpanel.png";
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        Vector3 site = Play.FlatSpot(_game.Start, 40f, 0.6f, 0.4f, span: 14f);
        _game.Survivor.PlaceOutside((site + new Vector3(0f, 1f, 9f)).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        Catalogue cat = _game.Zone.Data.Catalogue;
        var tune = _game.Zone.Data.Handling;
        float g = _game.Zone.Physics.Zone.Strength;
        Structure big = CoreCraft.Ship(cat, true, x => ModeFit.Missing(x, CoreMode.Flight, tune, g).Count == 0);
        BlockType lamp = cat.Types.First(t => t != null && t.IsLight);
        var cells = new[] { new Int3(-1, 0, -3), new Int3(0, 0, -3), new Int3(1, 0, -3) };
        foreach (Int3 c in cells) big.Set(c, Building.Make(big, lamp));
        big.SetLight(cells[0], LightSettings.Make(0xff3322, LightAnim.Pulse, 0.5f));
        big.DirtyChunks.Clear();
        Vessel b = Vessel.SpawnBase(_game.Zone, big, (_game.Zone.GroundAt(site.X, site.Z) + Vector3.Up * 1.4f).ToNumerics(), 0f);
        foreach (var (p, _) in big.Blocks().ToList()) b.ClearGround(p);
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (int x in _drive.Board(b)) yield return x;
        foreach (int x in _drive.Wait(20)) yield return x;
        _game.LightEdit = cells[0];
        foreach (int x in Play.OpenMenu(MenuTab.Core)) yield return x;
        foreach (int x in _drive.Wait(6)) yield return x;
        foreach (int x in Shot(file)) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
    }
}
