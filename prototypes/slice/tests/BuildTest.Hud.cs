using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Ui;
using Slice.Vessels;

namespace Slice.Tests;

/// <summary>
/// The visor's corners (design: Visor HUD layout): the sleeper's gauges and icon row at the bottom left, the surroundings
/// at the upper left, the toolbar at the bottom middle, and seated, the structure's gauges and icons at the bottom right,
/// mirroring the sleeper's. Each shows what the game has: helmet open and shut, in vacuum or not, near one of your cores
/// or not, seated or on foot.
/// </summary>
public partial class BuildTest
{
    IEnumerable<int> HudCornerChecks()
    {
        GD.Print("-- the visor's corners --");
        var size = new Vector2(1920, 1080);
        HudLayout l = HudLayout.Compute(size, 1f);
        float s = l.Scale;
        Check(l.Sleeper.Position.X < size.X * 0.2f && MathF.Abs(l.Sleeper.End.Y - (size.Y - HudLayout.Foot * s)) < 0.5f
              && l.SleeperIcons.End.Y < l.Sleeper.Position.Y && l.SleeperIcons.Position.X == l.Sleeper.Position.X
              && l.Surroundings.Position.X < size.X * 0.2f && l.Surroundings.End.Y < size.Y * 0.1f
              && MathF.Abs(l.Toolbar.GetCenter().X - size.X / 2f) < 0.5f && l.Toolbar.End.Y > l.Sleeper.Position.Y
              && MathF.Abs(size.X - l.Structure.End.X - l.Sleeper.Position.X) < 0.5f && l.Structure.End.Y == l.Sleeper.End.Y && l.StructureIcons.End.Y < l.Structure.Position.Y
              && HudLayout.IconAt(l.StructureIcons, 0, s, true).End.X == l.StructureIcons.End.X,
            $"1080p: sleeper gauges bottom left {l.Sleeper}, their icons above them, surroundings upper left {l.Surroundings}, toolbar bottom middle {l.Toolbar}, structure gauges bottom right {l.Structure} mirroring the sleeper's, icons from the corner in");

        var suit = _game.Suit;
        var gear = suit.Sim.Player.Gear;
        Gauge[] g = _game.Visor.SleeperGauges();
        var v = suit.Vitals;
        Check(string.Join(",", g.Select(x => x.Label)) == "HEALTH,STAMINA,FOOD,AIR,RADS"
              && g[0].Value == $"{v.Health:F0}" && g[1].Value == $"{v.Stamina:F0}" && g[2].Value == $"{suit.FoodShare * 100:F0}%"
              && g[3].Value == $"{suit.OxygenShare * 100:F0}%" && g[4].Value == $"{v.Radiation:F1}" && g.All(x => x.Share is >= 0 and <= 1.0001),
            $"sleeper gauges: {string.Join("  ", g.Select(x => $"{x.Label} {x.Value}"))}, each with its bar");
        Gauge[] here = _game.Visor.Surroundings();
        var e = suit.Last.Exposure;
        Check(here.Length == 2 && here[0].Label == "RAD" && here[0].Value == $"{e.Radiation:F1}" && here[1].Label == "TEMP" && here[1].Value == $"{e.Temperature:F0}°C",
            $"surroundings: RAD {here[0].Value}, TEMP {here[1].Value} ({e.Place})");

        // The helmet, open and shut.
        if (gear.HelmetOn) suit.Sim.ToggleHelmet();
        foreach (int x in _drive.Wait(3)) yield return x;
        IconState[] open = _game.Visor.SleeperIcons();
        suit.Sim.ToggleHelmet();
        foreach (int x in _drive.Wait(3)) yield return x;
        IconState[] shut = _game.Visor.SleeperIcons();
        Check(open[0].Icon == HudIcon.HelmetOpen && shut[0].Icon == HudIcon.HelmetShut && open[1].Icon == HudIcon.Vacuum && open[2].Icon == HudIcon.Core,
            $"the helmet icon shows open ({open[0].Icon}) and shut ({shut[0].Icon}), then vacuum and core");

        // Vacuum: space outside instead of the planet, the helmet shut; and back.
        bool before = shut[1].On;
        suit.Beyond = _game.Spaceflight.Space;
        foreach (int x in _drive.Wait(4)) yield return x;
        bool inSpace = _game.Visor.SleeperIcons()[1].On;
        suit.Beyond = null;
        foreach (int x in _drive.Wait(4)) yield return x;
        Check(!before && inSpace && !_game.Visor.SleeperIcons()[1].On, $"the vacuum icon is dark on the planet's air, lit with space outside, dark again on the way back");

        // Near one of your cores, and not: a floor round a core with a chair on it, built away from everything else.
        Vector3 site = _play.FlatSpot(_game.Start, 260f, 2.2f, 0.4f, span: 14f);
        _game.Survivor.PlaceOutside((site + new Vector3(0f, 3f, -40f)).ToNumerics());
        foreach (int x in _drive.Wait(20)) yield return x;
        bool farOff = _game.Visor.NearCore;
        var chair = _game.Zone.Data.Catalogue.Types.First(t => t != null && Vessel.IsChair(t));
        Vessel floor = SeatedTest.Floor(_game, chair, site, 0);
        floor.Structure.Owner = GalHack.Structures.Building.DefaultOwner; // as a core placed by hand makes it
        _game.Survivor.PlaceOutside((floor.Body.Position.ToGodot() + new Vector3(2f, 1.5f, 2f)).ToNumerics());
        foreach (int x in _drive.Wait(20)) yield return x;
        bool onIt = _game.Visor.NearCore && _game.Visor.SleeperIcons()[2].On;
        Check(!farOff && onIt, $"the core icon is dark 40 m from any of your cores and lit on your own base ({_game.CoresInReach.Count} in reach)");

        // Seated: the structure's gauges and icons, on its own state.
        floor.TakeChair(SeatedTest.Anchor);
        Check(_game.Survivor.Sit(floor), "seated in the base's chair");
        foreach (int x in _drive.Wait(10)) yield return x;
        Gauge[] sg = _game.Visor.StructureGauges(floor);
        Check(string.Join(",", sg.Select(x => x.Label)) == "CAP,FUEL,WARP,FOLD" && sg.All(x => x.Value == "--"),
            $"structure gauges: {string.Join("  ", sg.Select(x => $"{x.Label} {x.Value}"))} (no live power to read until power phase 1)");
        IconState[] icons = _game.Visor.StructureIcons(floor);
        bool gravity = floor.Structure.GravityOn;
        floor.Structure.GravityOn = !gravity;
        bool flipped = _game.Visor.StructureIcons(floor)[1].On;
        floor.Structure.GravityOn = gravity;
        floor.PowerOn = false;
        bool off = _game.Visor.StructureIcons(floor)[0].On;
        floor.PowerOn = true;
        Check(string.Join(",", icons.Select(x => x.Icon)) == "Power,Gravity,Atmosphere,ThrustersPassive"
              && icons[0].On == floor.PowerOn && (icons[0].Tone == UiTheme.Colors.Good) == floor.Powered && icons[1].On == gravity && flipped == !gravity && !off
              && icons[2].On == _game.Suit.HoldsAir(floor) && !icons[3].On,
            $"structure icons: power {(icons[0].On ? floor.Powered ? "on" : "on, none to give" : "off")} (dark switched off), gravity {(gravity ? "on" : "off")} (follows its switch), air {(icons[2].On ? "on" : "off")}, thrusters dark on a base");
        _game.Survivor.StandUp();
        foreach (int x in _drive.Wait(5)) yield return x;
        Check(_game.Survivor.Seated == null, "stood up: the structure's corner is the seat's alone");
    }
}
