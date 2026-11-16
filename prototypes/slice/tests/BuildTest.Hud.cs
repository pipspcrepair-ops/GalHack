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
/// The visor's corners (design: Visor HUD layout): the sleeper's gauges and icon row at the bottom left, the temperature
/// at the upper left, the toolbar at the bottom middle, and seated, the structure's gauges and icons at the bottom right,
/// mirroring the sleeper's. Each shows what the game has: helmet open and shut, in vacuum or not, near one of your cores
/// or not, seated or on foot.
/// </summary>
public partial class BuildTest
{
    /// <summary>
    /// The Sleeper tool on a block shows its hit points over its most just above the reticle (owner): the
    /// right numbers, following a hit and a repair, in both modes, and gone off the block or out of range.
    /// </summary>
    IEnumerable<int> HudBlockHealth()
    {
        GD.Print("-- the block the tool is on: hit points over the reticle --");
        Vector3 site = _play.FlatSpot(_game.Start, 30f, span: 10f);
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(site.X, site.Z) + Vector3.Up * 0.3f).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        foreach (int x in _play.Tool(Slice.Player.ToolMode.Repair)) yield return x;
        Vessel v = TestRows.Row(_game, GalHack.Structures.Building.DefaultOwner, true, (float)new Random().NextDouble() * MathF.Tau, out GalHack.Structures.Int3[] cells);
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _drive.LookAt(TestRows.FaceOf(_game, v, cells[1]), 8)) yield return x;
        GalHack.Structures.Int3 on = Slice.Game.WorldRay.Cast(_game.Zone, _game.Survivor, 6f, Layers.World | Layers.Hull, out Slice.Game.LookHit look) && look.Vessel == v ? look.Cell : cells[1];
        var s = v.Structure;
        float max = s.StatsOf(s.Get(on)).MaxHp;
        string Shown() => _game.Visor.BlockHealth();
        string whole = Shown();
        s.SetHp(on, max * 0.4f);
        v.ApplyEdits();
        foreach (int x in _drive.Wait(2)) yield return x;
        string hit = Shown();
        foreach (var (id, n) in v.Hits.RepairBill(on)) Pack.Add(id, n);
        foreach (int x in _drive.HoldPrimary(TestRows.FaceOf(_game, v, on), 40)) yield return x;
        string mended = Shown();
        Check(whole == $"{max:F0} / {max:F0}" && hit == $"{MathF.Ceiling(max * 0.4f):F0} / {max:F0}" && mended == whole,
            $"on a block the tool shows its hit points over its most above the reticle: {whole}, {hit} after a hit, {mended} repaired");
        foreach (int x in _play.Tool(Slice.Player.ToolMode.Gather)) yield return x;
        string gather = Shown();
        foreach (int x in _drive.LookAt(_game.Survivor.WorldEye() + Vector3.Up * 20f, 8)) yield return x;
        string sky = Shown();
        Vector3 back = _game.Survivor.WorldEye() - (TestRows.Centre(v, on) - _game.Survivor.WorldEye()).Normalized() * 8f;
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(back.X, back.Z) + Vector3.Up * 0.3f).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _drive.LookAt(TestRows.FaceOf(_game, v, on), 8)) yield return x;
        float far = _game.Survivor.WorldEye().DistanceTo(TestRows.Centre(v, on));
        string beyond = Shown();
        Check(gather == whole && sky == null && (far <= _game.Zone.Data.Rules.Tool.Lightning.Range || beyond == null),
            $"in attack/gather too ({gather}); off the block nothing ({sky ?? "none"}), nor out of range ({beyond ?? "none"} at {far:F1} m)");
    }

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
            $"1080p: sleeper gauges bottom left {l.Sleeper}, their icons above them, temperature upper left {l.Surroundings}, toolbar bottom middle {l.Toolbar}, structure gauges bottom right {l.Structure} mirroring the sleeper's, icons from the corner in");

        var suit = _game.Suit;
        var gear = suit.Sim.Player.Gear;
        Gauge[] g = _game.Visor.SleeperGauges();
        var v = suit.Vitals;
        Check(string.Join(",", g.Select(x => x.Label)) == "HEALTH,STAMINA,FOOD,AIR,RADS"
              && g[0].Value == $"{v.Health:F0}" && g[1].Value == $"{v.Stamina:F0}" && g[2].Value == $"{suit.FoodShare * 100:F0}%"
              && g[3].Value == $"{suit.OxygenShare * 100:F0}%" && g[4].Value == $"{v.Radiation:F1}" && g.All(x => x.Share is >= 0 and <= 1.0001),
            $"sleeper gauges: {string.Join("  ", g.Select(x => $"{x.Label} {x.Value}"))}, each with its bar");
        Gauge here = _game.Visor.Surroundings();
        var e = suit.Last.Exposure;
        Check(here.Label == "TEMP" && here.Value == $"{e.Temperature:F0}°C" && l.Surroundings.Size.Y <= HudLayout.SurroundRowH * s + 0.5f,
            $"upper left, the temperature alone: TEMP {here.Value} ({e.Place}); radiation is the sleeper's RADS gauge");

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
            $"structure gauges: {string.Join("  ", sg.Select(x => $"{x.Label} {x.Value}"))} (no generator and no drives on it: nothing to read)");
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
