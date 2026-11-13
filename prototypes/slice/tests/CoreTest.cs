using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Items;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Game;
using Slice.Items;
using Slice.Ui;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// The one core, three modes (docs/one-metre-grid.md section 4): every structure has Base, Hover
/// and Flight Mode, and what a mode lacks is only a warning. Only one core exists in the data, and
/// old ids still load. Every kind of structure switches to all three through the CORE tab's switch,
/// nothing refused; one craft flips both ways on the ground and in flight, with no block rebuilt;
/// it falls if it isn't fitted for the mode; hover stays near the ground and can't reach orbit, a
/// 20 t hover tank included; a base flies and comes down as a base again. The structures are made from blocks directly, on a fresh
/// random world: this tests the modes, and the switch is clicked through the game's own menu.
/// </summary>
public partial class CoreTest : Node
{
    Game.Game _game;
    Driver _drive;
    Playthrough _play;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Vector3 _site;

    GameData Data => _game?.Zone.Data ?? GameData.Shared;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100;
        _run = Scenarios().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"  FAIL  {what}");
    }

    bool Check(bool ok, string what)
    {
        if (ok) { _passes++; GD.Print($"  ok    {what}"); }
        else Fail(what);
        return ok;
    }

    IEnumerable<int> Scenarios()
    {
        GD.Print("-- exactly one core --");
        OneCore();
        GD.Print("-- old ids and old blueprints load --");
        OldData();

        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        _play = new Playthrough(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        _site = _play.FlatSpot(_game.Start, 70f, 0.6f, 0.4f, span: 14f);
        GD.Print($"  (a clearing: {Playthrough.Clearing(_game.Zone, _site, 50f)} trees and boulders felled)"); // room to fly: vessels collide with trunks and crowns
        _game.Survivor.PlaceOutside((_site + new Vector3(0f, 1f, 8f)).ToNumerics());
        foreach (int x in _drive.Wait(90)) yield return x;

        foreach (int x in Craft()) yield return x;
        foreach (int x in Base()) yield return x;
        foreach (int x in AllModes()) yield return x;
        foreach (int x in LargeHover()) yield return x;
    }

    void OneCore()
    {
        ItemCatalogue items = Data.Items;
        var cores = items.Items.Where(i => i.BlockId == "core").Select(i => (i.Id, i.Name)).ToList();
        Check(cores.SequenceEqual(new[] { ("core", "Core") }) && PlayerInventory.Core == "core", $"the core block has one item: {string.Join(", ", cores)}");
        string[] gone = { "big-core", "small-core", "base-core", "capital-core", "vessel-core", "hover-core" };
        Check(!items.Items.Any(i => gone.Contains(i.Id)), "no old core is an item of its own");
        var named = items.Items.Where(i => i.Id != "core" && i.Id != "fusion-core" && i.Id != "lattice-core" && i.Name.EndsWith(" core", StringComparison.OrdinalIgnoreCase)).Select(i => i.Name).ToList();
        Check(named.Count == 0, $"nothing else calls itself a structure's core{(named.Count > 0 ? ": " + string.Join(", ", named) : "")}");
        var tree = new Progress(Data.Progress, items, Data.Catalogue);
        var nodes = tree.Nodes.Where(n => n.BlockId == "core").Select(n => (n.Id, n.Tab)).ToList();
        Check(nodes.SequenceEqual(new[] { ("core", "structures") }), $"one core node, on the Structures tab: {string.Join(", ", nodes)}");
        Check(!tree.Nodes.Any(n => gone.Contains(n.Id) || gone.Contains(n.Unlock.Parent)), "no node is or hangs from an old core");
        Check(tree.Node("core").Level == 1 && items.Item("core").ShortName.Length <= 8, $"it opens at level 1, and its short name fits a slot: {items.Item("core").ShortName}");
    }

    void OldData()
    {
        ItemCatalogue items = Data.Items;
        foreach (string old in new[] { "big-core", "small-core", "base-core", "capital-core", "vessel-core", "hover-core" })
            Check(items.Item(old).Id == "core", $"the old id {old} reads as the core");
        var saved = new ProgressState { PointsEarned = 9, PointsSpent = 3, Unlocked = new List<string> { "small-core", "hover@small" } };
        var tree = new Progress(Data.Progress, items, Data.Catalogue, saved);
        Check(tree.IsUnlocked("core") && tree.IsUnlocked("hover-t1"), "a saved tech tree keeps its core and per-grid blocks unlocked under the new ids");

        // A blueprint from before: "mode capital" on a small grid, a hover pad on the block list.
        const string oldPrint = "schema galhack.blueprint\nversion 1\nname \"Old rover\"\ngrid small\nmode capital\nblock 0 0 0 core\nblock 0 -1 0 hover\n";
        Structure rover = Blueprint.Load(oldPrint, Data.Catalogue, out List<string> errors);
        Check(rover?.Mode == CoreMode.Hover, $"an old blueprint with hover pads loads in Hover Mode {string.Join("; ", errors)}");
        Structure flyer = Blueprint.Load(oldPrint.Replace("block 0 -1 0 hover\n", ""), Data.Catalogue, out _);
        Check(flyer?.Mode == CoreMode.Flight, "an old small blueprint without them flies, in Flight Mode");
        Check(Blueprint.Load(Blueprint.Save(rover), Data.Catalogue, out _)?.Mode == CoreMode.Hover, "and a saved one keeps its mode");
        Structure skiff = CoreCraft.Skiff(Data);
        Check(skiff.Mode == CoreMode.Flight && skiff.Core != null, "the Skiff's blueprint still loads, in Flight Mode");
    }

    /// <summary>Clicks a mode on the CORE tab, seated or not.</summary>
    IEnumerable<int> Switch(Vessel v, CoreMode mode)
    {
        foreach (int x in _play.OpenMenu(MenuTab.Core)) yield return x;
        int i = _game.Menu.Root.Core.Near().IndexOf(v);
        if (i >= 0 && _game.Menu.Root.Core.Picked != v) foreach (int x in _drive.ClickWidget($"core:pick:{i}")) yield return x;
        foreach (int x in _drive.ClickWidget($"core:mode:{mode.ToString().ToLowerInvariant()}")) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
    }

    /// <summary>Into a structure's chair: on foot if the site lets it, directly if a tree or a bank is in the way (the seat isn't what this tests).</summary>
    IEnumerable<int> Seat(Vessel v)
    {
        foreach (int x in _drive.Board(v)) yield return x;
        if (_game.Survivor.Seated == v) yield break;
        GD.Print($"  (couldn't walk to the chair here, {v.NearestChair(_game.Survivor.WorldEye(), out _):F1} m off: seated directly)");
        v.TakeChair(v.Chairs.First());
        _game.Survivor.Sit(v);
        foreach (int x in _drive.Wait(5)) yield return x;
    }

    /// <summary>What may hold a craft up or down: the trees standing within 12 m of it (vessels collide with their trunks and crowns), and what is under it.</summary>
    string Near(Vessel v)
    {
        Vector3 at = v.Body.Position.ToGodot();
        var q = PhysicsRayQueryParameters3D.Create(at, at + Vector3.Down * 40f, uint.MaxValue, new Godot.Collections.Array<Rid> { v.Binding.Collider.GetRid() });
        var hit = _game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(q);
        string under = hit.Count == 0 ? "nothing" : hit["collider"].AsGodotObject() is CollisionObject3D c ? $"{c.GetPath()} (layer {c.CollisionLayer}) {at.Y - hit["position"].AsVector3().Y:F1} m down" : "?";
        return $"{_game.Zone.Streamer.FloraNear(at, 12f).Count()} trees within 12 m, under it {under}, at {at}, {v.Body.Velocity.Length():F1} m/s";
    }

    IEnumerable<int> Hold(Key key, int ticks)
    {
        _game.Controls.Hold(key);
        for (int i = 0; i < ticks; i++) yield return 0;
        _game.Controls.Hold(key, false);
    }

    IEnumerable<int> Craft()
    {
        GD.Print("-- a craft flies, then falls when it isn't fitted for its mode --");
        Structure s = CoreCraft.Skiff(Data);
        Vector3 at = _site + Vector3.Up * 3f;
        Vessel v = Vessel.Spawn(_game.Zone, s, at.ToNumerics(), 0f, landed: true);
        v.Settle();
        foreach (int x in _drive.Wait(10)) yield return x;
        var blocks = CoreCraft.Blocks(s);
        foreach (int x in Seat(v)) yield return x;
        Check(_game.Survivor.Seated == v, "in its chair");
        if (_game.Survivor.Seated != v) yield break;
        foreach (int x in _play.OpenMenu(MenuTab.Core)) yield return x;
        Check(OffersAll(), "with no hover plates, the CORE tab offers Base, Hover and Flight");
        foreach (int x in _play.CloseMenu()) yield return x;
        Check(!v.Stats.IsHover && v.Controller.Gear != null && v.Controller.Hover == null, "the Skiff is in Flight Mode, on its legs");

        foreach (int x in Hold(Key.Space, 60)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        float high = v.AboveGround;
        Check(high > 5f, $"thrusters lift it in Flight Mode ({high:F0} m up)");
        // There is no gear button: G does nothing, and nothing anywhere can toggle the gear.
        bool gearWas = v.Controller.Gear.Down;
        foreach (int x in Hold(Key.G, 3)) yield return x;
        Check(v.Controller.Gear.Down == gearWas && typeof(VesselController).GetMethod("ToggleGear") == null && typeof(LandingGear).GetMethod("Toggle") == null,
            $"G is no gear key: the gear stays {(gearWas ? "out" : "in")} by its own rule ({v.AboveGround:F0} m up)");
        blocks = CoreCraft.Blocks(s); // a branch on the way up is no part of this: the blocks as they are now
        foreach (int x in Switch(v, CoreMode.Hover)) yield return x;
        Check(s.Mode == CoreMode.Hover && CoreCraft.SameBlocks(s, blocks), "put in Hover Mode in flight, with no hover plates: nothing refuses it, no block changed");
        foreach (int x in _drive.Wait(2)) yield return x; // the handling model reads the new mode on its next step
        Check(v.Stats.IsHover && !v.Stats.CanLiftOff && v.ModeWarning?.Contains("HOVER PLATES") == true, $"the console says what's missing: \"{v.ModeWarning}\"");
        float falling = 0f;
        for (int t = 0; t < 60 * 12 && !v.Controller.Frozen; t++)
        {
            falling = MathF.Min(falling, v.Body.Velocity.Y);
            yield return 0;
        }
        Check(falling < -4f, $"it loses lift and falls ({falling:F1} m/s at worst)");
        Check(v.AboveGround < 4f, $"and comes down ({v.AboveGround:F1} m up; {Near(v)})");

        // The fall is real: what it broke is the player's doing, so the blocks are counted afresh from here.
        blocks = CoreCraft.Blocks(s);
        foreach (int x in Switch(v, CoreMode.Flight)) yield return x;
        Check(s.Mode == CoreMode.Flight && CoreCraft.SameBlocks(s, blocks), "the player can switch back");
        foreach (int x in Hold(Key.Space, 120)) yield return x;
        Check(v.AboveGround > 6f, $"and it flies again ({v.AboveGround:F0} m up)");
        foreach (int x in _drive.Wait(30)) yield return x;
        _game.Survivor.StandUp();

        GD.Print("-- fitted with hover plates and thrusters, one craft does both --");
        // A fresh craft: the fall above can break anything on the first, legs included.
        _site = _play.FlatSpot(_game.Start, 110f, 2.4f, 0.4f, span: 14f);
        GD.Print($"  (a clearing: {Playthrough.Clearing(_game.Zone, _site, 50f)} trees and boulders felled)");
        _game.Survivor.PlaceOutside((_site + new Vector3(0f, 1f, 8f)).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        s = CoreCraft.Skiff(Data);
        Check(CoreCraft.FitPlates(s, Data.Catalogue) == 4, "four hover plates fitted under a Skiff, thrusters and legs as they were");
        s.DirtyChunks.Clear();
        v = Vessel.Spawn(_game.Zone, s, (_site + Vector3.Up * 3f).ToNumerics(), 0f, landed: true);
        v.Settle();
        blocks = CoreCraft.Blocks(s);
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in Seat(v)) yield return x;
        Check(_game.Survivor.Seated == v, "in its chair");
        if (_game.Survivor.Seated != v) yield break;
        Check(v.Controller.Hover == null && v.Controller.Gear != null, "hover plates do nothing in Flight Mode: it stands on its legs");
        var lift = new PlateRead(v);
        Check(lift.Plates == 0, "the flight model sees no hover plates in Flight Mode");

        foreach (int x in Switch(v, CoreMode.Hover)) yield return x;
        Check(v.Controller.Hover != null && v.Controller.Gear == null && v.Stats.IsHover && v.Stats.CanLiftOff, $"Hover Mode: the same craft on its plates, its legs folded (lift margin {v.Stats.LiftMargin:+0} kN)");
        Check(v.Stats.UpwardThrust == lift.PlateLift, "in Hover Mode lift is the plates' alone: thrusters only push it along");
        Vector3 from = v.Body.Position.ToGodot();
        foreach (int x in Hold(Key.W, 60)) yield return x;
        foreach (int x in _drive.Wait(60)) yield return x;
        Vector3 to = v.Body.Position.ToGodot();
        float ride = v.Stats.RideHeight;
        Check(new Vector2(to.X - from.X, to.Z - from.Z).Length() > 4f, $"thrusters push it along ({new Vector2(to.X - from.X, to.Z - from.Z).Length():F0} m)");
        Check(v.Controller.Hover.Grounded, $"riding on its plates ({v.Controller.Hover.Clearance:F2} m clear, rides at {ride:F1}; {Near(v)})");
        foreach (int x in Hold(Key.Space, 180)) yield return x;
        Check(v.AboveGround < 8f, $"Space lifts it no higher: thrusters don't climb in Hover Mode ({v.AboveGround:F1} m up)");
        foreach (int x in _drive.Wait(90)) yield return x;

        _game.Controls.Hold(Key.W);
        foreach (int x in _drive.Wait(40)) yield return x;
        N.Vector3 before = v.Body.Velocity;
        foreach (int x in Switch(v, CoreMode.Flight)) yield return x;
        _game.Controls.Hold(Key.W, false);
        Check(s.Mode == CoreMode.Flight && v.Controller.Hover == null && v.Controller.Gear != null && !v.Controller.Frozen, "switched to Flight Mode on the move: its thrusters take over at once");
        Check(new Vector2(v.Body.Velocity.X, v.Body.Velocity.Z).Length() > new Vector2(before.X, before.Z).Length() * 0.4f, $"it keeps going, no reset ({before.Length():F0} m/s then {v.Body.Velocity.Length():F0})");
        foreach (int x in Hold(Key.Space, 70)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(v.AboveGround > 8f, $"Flight Mode climbs ({v.AboveGround:F0} m up; {Near(v)})");
        float was = v.Body.Position.Y;
        blocks = CoreCraft.Blocks(s);
        foreach (int x in Switch(v, CoreMode.Hover)) yield return x;
        Check(s.Mode == CoreMode.Hover && CoreCraft.SameBlocks(s, blocks), "flipped in both directions in flight, no block rebuilt");
        bool sank = false;
        for (int t = 0; t < 60 * 12 && !(v.Controller.Hover?.Grounded ?? false); t++)
        {
            sank |= v.Body.Position.Y < was - 1f;
            yield return 0;
        }
        Check(sank && v.Controller.Hover?.Grounded == true, "switched to Hover Mode up there it sinks until the ground is back in range, then rides");
        foreach (int x in _drive.Wait(120)) yield return x;
        Check(v.AboveGround < 6f, $"and stays down there on its plates ({v.AboveGround:F1} m up)");

        GD.Print("-- hover can't reach orbit --");
        Check(!_game.InOrbit, "on the ground to begin with");
        for (int t = 0; t < 60 * 90 && _game.Orbit?.Ready != true; t++)
        {
            // Carried to the edge of space in Hover Mode, which no climb could do: it still doesn't cross.
            v.Body.Position = v.Body.Position with { Y = _game.Surface.Physics.Zone.Ceiling + 2f };
            v.Binding.Teleport();
            v.Body.Velocity = new N.Vector3(0f, 40f, 0f);
            yield return 0;
        }
        Check(_game.Orbit?.Ready == true, "orbit is loaded and waiting");
        for (int t = 0; t < 120; t++)
        {
            v.Body.Velocity = new N.Vector3(0f, 40f, 0f);
            yield return 0;
        }
        Check(!_game.InOrbit && _game.Zone == _game.Surface, "a craft in Hover Mode doesn't cross into orbit, whatever carried it up");
        Check(v.Body.Position.Y < _game.Surface.Physics.Zone.Ceiling, $"it is held under the ceiling and falls from there ({v.Body.Position.Y:F0} m, ceiling {_game.Surface.Physics.Zone.Ceiling:F0})");
        _game.Survivor.StandUp();
        foreach (int x in _drive.Wait(5)) yield return x;
    }

    IEnumerable<int> Base()
    {
        GD.Print("-- a base flies, and comes down a base --");
        _site += new Vector3(30f, 0f, 0f);
        Vector3 ground = _game.Zone.GroundAt(_site.X, _site.Z);
        _game.Survivor.PlaceOutside((ground + new Vector3(0f, 1f, 10f)).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        ShipHandling.Handling.Tunables tune = Data.Handling;
        float g = _game.Zone.Physics.Zone.Strength;
        Structure s = CoreCraft.Ship(Data.Catalogue, thrusters: true, x => ModeFit.Missing(x, CoreMode.Flight, tune, g).Count == 0);
        Check(ModeFit.Missing(s, CoreMode.Flight, tune, g).Count == 0, $"a base with thrusters, a chair and a gyro on it ({s.Count} blocks): {string.Join(", ", ModeFit.Missing(s, CoreMode.Flight, tune, g))}");
        Vector3 centre = ground + Vector3.Up * (s.Grid.CellSize * 0.5f + 0.4f);
        Vessel v = Vessel.SpawnBase(_game.Zone, s, centre.ToNumerics(), 0f);
        foreach (var (p, _) in s.Blocks().ToList()) v.ClearGround(p);
        var blocks = CoreCraft.Blocks(s);
        foreach (int x in _drive.Wait(20)) yield return x;
        Check(v.IsBase && v.Flight == null && s.Mode == CoreMode.Base && StructureStats.Compute(s).Thrust.Sum() == 0, "in Base Mode it is anchored, its thrusters idle");
        foreach (int x in Seat(v)) yield return x;
        Check(_game.Survivor.Seated == v, "in its chair");
        if (_game.Survivor.Seated != v) yield break;
        foreach (int x in _play.OpenMenu(MenuTab.Core)) yield return x;
        Check(OffersAll(), "a base too: the CORE tab offers Base, Hover and Flight");
        foreach (int x in _play.CloseMenu()) yield return x;

        foreach (int x in Switch(v, CoreMode.Flight)) yield return x;
        Check(s.Mode == CoreMode.Flight && !v.IsBase && v.Flight != null && v.Body.Motion == GalHack.Physics.Motion.Kinematic && CoreCraft.SameBlocks(s, blocks), "switched to Flight Mode: the same structure on the flight model, no block changed");
        Check(_game.Survivor.Seated == v, "the pilot stays in their chair");
        float rest = v.Body.Position.Y;
        foreach (int x in Hold(Key.Space, 30)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(v.Body.Position.Y > rest + 4f, $"it lifts off ({v.Body.Position.Y - rest:F1} m up)");
        foreach (int x in _drive.Wait(60)) yield return x;

        blocks = CoreCraft.Blocks(s);
        foreach (int x in Switch(v, CoreMode.Base)) yield return x;
        Check(s.Mode == CoreMode.Base && v.Unanchored && CoreCraft.SameBlocks(s, blocks), "switched to Base Mode in flight: nothing refuses it");
        float falling = 0f;
        for (int t = 0; t < 60 * 15 && !v.IsBase; t++)
        {
            falling = MathF.Min(falling, v.Body.Velocity.Y);
            yield return 0;
        }
        Check(falling < -2f, $"its engines cut and it falls ({falling:F1} m/s at worst)");
        Check(v.IsBase && v.Body.Motion == GalHack.Physics.Motion.Fixed && v.Flight == null, $"it lands and anchors there, a base again ({s.Count} blocks standing)");
        blocks = CoreCraft.Blocks(s);
        Check(_game.Survivor.Seated == v, "with its pilot still seated");
        Check(v.AboveGround < 6f, $"on the ground ({v.AboveGround:F1} m up)");
        foreach (int x in _drive.Wait(30)) yield return x;

        foreach (int x in Switch(v, CoreMode.Flight)) yield return x;
        rest = v.Body.Position.Y;
        foreach (int x in Hold(Key.Space, 60)) yield return x;
        Check(v.Body.Position.Y > rest + 4f && CoreCraft.SameBlocks(s, blocks), "and back again: it lifts off a second time");
        _game.Survivor.StandUp();
        foreach (int x in _drive.Wait(5)) yield return x;

        GD.Print("-- a base with nothing to fly on --");
        _site += new Vector3(-60f, 0f, 0f);
        ground = _game.Zone.GroundAt(_site.X, _site.Z);
        _game.Survivor.PlaceOutside((ground + new Vector3(0f, 1f, 8f)).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        Structure bare = CoreCraft.Ship(Data.Catalogue, thrusters: false, _ => true);
        bare.Name = "Bare base";
        Vessel b = Vessel.SpawnBase(_game.Zone, bare, (ground + Vector3.Up * 1.4f).ToNumerics(), 0f);
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in Switch(b, CoreMode.Flight)) yield return x;
        Check(bare.Mode == CoreMode.Flight && b.ModeWarning?.Contains("THRUSTERS") == true, $"Flight Mode isn't refused; the console warns: \"{b.ModeWarning}\"");
        Check(!b.IsBase && b.Flight != null && b.Controller.Frozen, "it sits where it stood");
        foreach (int x in Switch(b, CoreMode.Base)) yield return x;
        Check(bare.Mode == CoreMode.Base && b.IsBase && b.ModeWarning == null, "and switches straight back: Base Mode needs nothing");
    }

    /// <summary>The CORE tab, open, shows a button for each of the three modes.</summary>
    bool OffersAll() => ModeFit.Modes.Length == 3 && ModeFit.Modes.All(m => _game.Menu.RectOf($"core:mode:{m.ToString().ToLowerInvariant()}") != null);

    /// <summary>
    /// Every kind of structure has all three modes: a craft with thrusters and legs, a base with
    /// nothing to fly on, and a hover tank. The CORE tab offers each of them all three, switching to
    /// any is never refused, and what a mode lacks is listed as its warning.
    /// </summary>
    IEnumerable<int> AllModes()
    {
        GD.Print("-- every structure has all three modes; what one lacks is a warning --");
        _site = _play.FlatSpot(_game.Start, 140f, 2.4f, 0.4f, span: 20f);
        GD.Print($"  (a clearing: {Playthrough.Clearing(_game.Zone, _site, 50f)} trees and boulders felled)");
        _game.Survivor.PlaceOutside((_site + new Vector3(0f, 1f, 2f)).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        float g = _game.Zone.Physics.Zone.Strength;
        Structure skiff = CoreCraft.Skiff(Data), bare = CoreCraft.Ship(Data.Catalogue, thrusters: false, _ => true), tank = CoreCraft.HoverTank(Data.Catalogue, 22f, g);
        bare.Name = "Bare base";
        var kinds = new (Structure s, Vector3 at, string[] hover, string[] flight)[]
        {
            (skiff, _site + new Vector3(-14f, 0f, -8f), new[] { "HOVER PLATES" }, Array.Empty<string>()),
            (bare, _site + new Vector3(0f, 0f, -12f), new[] { "HOVER PLATES", "THRUSTERS" }, new[] { "THRUSTERS" }),
            (tank, _site + new Vector3(14f, 0f, -8f), Array.Empty<string>(), new[] { "UP THRUST" }),
        };
        var spawned = new List<Vessel>();
        foreach (var k in kinds)
        {
            k.s.DirtyChunks.Clear();
            Vessel v = Vessel.Spawn(_game.Zone, k.s, (k.at + Vector3.Up * 4f).ToNumerics(), 0f, landed: true);
            v.Settle();
            spawned.Add(v);
        }
        foreach (int x in _drive.Wait(20)) yield return x;
        for (int i = 0; i < kinds.Length; i++)
        {
            Vessel v = spawned[i];
            Structure s = kinds[i].s;
            var blocks = CoreCraft.Blocks(s);
            foreach (int x in _play.OpenMenu(MenuTab.Core)) yield return x;
            int at = _game.Menu.Root.Core.Near().IndexOf(v);
            if (at >= 0) foreach (int x in _drive.ClickWidget($"core:pick:{at}")) yield return x;
            Check(at >= 0 && OffersAll(), $"{s.Name}: the CORE tab offers Base, Hover and Flight");
            foreach (int x in _play.CloseMenu()) yield return x;
            var said = new List<string>();
            bool all = true;
            foreach (CoreMode m in new[] { CoreMode.Hover, CoreMode.Flight, CoreMode.Base })
            {
                foreach (int x in Switch(v, m)) yield return x;
                all &= s.Mode == m;
                string warning = ModeFit.Warning(m, v.MissingFor(m));
                said.Add($"{m}: {warning ?? "ready"}");
                string[] lacks = m == CoreMode.Hover ? kinds[i].hover : m == CoreMode.Flight ? kinds[i].flight : Array.Empty<string>();
                all &= lacks.Length == 0 ? warning == null : warning != null && lacks.All(warning.Contains);
            }
            Check(all && CoreCraft.SameBlocks(s, blocks), $"{s.Name} switches to all three, nothing refused, no block changed; {string.Join("; ", said)}");
        }
        foreach (int x in _drive.Wait(5)) yield return x;
    }

    /// <summary>
    /// Large Hover: a hover tank of about 20 t rides its plates at their ride height, drives on its
    /// thrusters, and climbs no higher however long its pilot asks: plates lift only near the ground.
    /// </summary>
    IEnumerable<int> LargeHover()
    {
        GD.Print("-- Large Hover: a hover tank rides, drives, and can't climb out of its band --");
        _site = _play.FlatSpot(_game.Start, 200f, 2.4f, 0.4f, span: 24f);
        GD.Print($"  (a clearing: {Playthrough.Clearing(_game.Zone, _site, 60f)} trees and boulders felled)");
        _game.Survivor.PlaceOutside((_site + new Vector3(0f, 1f, 12f)).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        Structure s = CoreCraft.HoverTank(Data.Catalogue, 22f, _game.Zone.Physics.Zone.Strength);
        s.DirtyChunks.Clear();
        float tonnes = (float)StructureStats.Compute(s).Mass;
        int plates = s.Blocks().Count(b => s.TypeOf(b.block).Function == "hover");
        Vessel v = Vessel.Spawn(_game.Zone, s, (_site + Vector3.Up * 4f).ToNumerics(), 0f, landed: true);
        v.Settle();
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(tonnes >= 20f && tonnes <= 40f && v.ModeWarning == null && v.Stats.IsHover && v.Stats.CanLiftOff && v.Stats.TopSpeed == Data.Handling.Speeds.Hover.TopSpeedAir,
            $"a {tonnes:F1} t hover tank on {plates} plates in Hover Mode lacks nothing (lift margin {v.Stats.LiftMargin:+0} kN, top speed {v.Stats.TopSpeed:F0} m/s)");
        foreach (int x in Seat(v)) yield return x;
        Check(_game.Survivor.Seated == v, "in its chair");
        if (_game.Survivor.Seated != v) yield break;

        Vector3 from = v.Body.Position.ToGodot();
        foreach (int x in Hold(Key.W, 120)) yield return x;
        foreach (int x in _drive.Wait(90)) yield return x;
        Vector3 to = v.Body.Position.ToGodot();
        float driven = new Vector2(to.X - from.X, to.Z - from.Z).Length(), ride = v.Stats.RideHeight;
        HoverController hover = v.Controller.Hover;
        Check(driven > 4f, $"its thrusters drive it ({driven:F0} m)");
        Check(hover != null && hover.Grounded && MathF.Abs(hover.Clearance - ride) < 0.6f,
            $"it rides its plates at their ride height ({hover?.Clearance:F2} m clear, rides at {ride:F1}; {Near(v)})");

        float highest = 0f;
        for (int t = 0; t < 4; t++)
        {
            foreach (int x in Hold(Key.Space, 60)) yield return x;
            highest = MathF.Max(highest, v.Controller.Hover?.Clearance ?? float.PositiveInfinity);
        }
        foreach (int x in Hold(Key.W, 60)) yield return x;
        highest = MathF.Max(highest, v.Controller.Hover?.Clearance ?? float.PositiveInfinity);
        Check(highest < ride + 2f && v.AboveGround < ride + 3f,
            $"held on Space for 4 s and driven on, it climbs no higher than its band: {highest:F1} m clear at most, {v.AboveGround:F1} m up (rides at {ride:F1})");
        _game.Survivor.StandUp();
        foreach (int x in _drive.Wait(5)) yield return x;
    }

    /// <summary>What the flight model sees of a craft's hover plates now.</summary>
    readonly struct PlateRead
    {
        public readonly int Plates;
        public readonly float PlateLift;

        public PlateRead(Vessel v)
        {
            Plates = v.Spec.HoverEngines.Count;
            var hover = new PhysicsSandbox.Bridges.StructureVessel(v.Structure, v.Zone.Data.Handling, CoreMode.Hover);
            PlateLift = hover.HoverEngines.Sum(e => e.Lift);
        }
    }
}
