using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Items;
using Slice.Ui;
using Slice.Vessels;
using N = System.Numerics;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The loop's screenshot views: the Tab menu on each tab, a body's loot, a bolt in flight, a shot
/// inside the pod, the head torch at night, a base and a vessel built by hand. Staged quickly:
/// things are handed over (marked STAGING) and Creative is used where it only saves time; every
/// placement and every menu click still goes through the game's own controls.
/// </summary>
public sealed partial class Staging
{
    Playthrough _play;
    Playthrough Play => _play ??= new Playthrough(_game);

    /// <summary>STAGING: things in the bag to show (the screenshots show menus and building, not gathering).</summary>
    void Hand(params (string id, int n)[] items)
    {
        foreach (var (id, n) in items) _game.Pack.Add(id, n);
    }

    /// <summary>The Tab menu on one tab (--tab=you|tech|stats|core|map), by the pod with a survival constructor set down and working.</summary>
    IEnumerable<int> Menu()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.33);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Hand(("iron-ore", 160), ("copper-ore", 80), ("carbon-ore", 70), ("silicon-ore", 30), ("biomass", 14), ("stone", 22), ("ice", 12), ("meat", 6), ("hide", 2));
        _game.Progress.Earn(420, "staging");
        foreach (int x in Play.OpenMenu(MenuTab.You)) yield return x;
        foreach (int x in Play.Order("fab", "survival-constructor", 1)) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
        _game.CraftTimeScale = 200;
        foreach (int x in Play.WaitFor(() => _game.Pack["survival-constructor"] > 0, 20)) yield return x;
        _game.CraftTimeScale = 1;
        foreach (int x in Play.ToToolbar("survival-constructor", 2)) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
        // Out from the hatch, then set it down a few metres on.
        Vector3 away = _game.Bunker.Ahead(5f);
        foreach (int x in _drive.WalkTo(away, 1f, 20f)) yield return x;
        Vector3 spot = _game.Zone.GroundAt(_game.Bunker.Ahead(9f, 2f).X, _game.Bunker.Ahead(9f, 2f).Z);
        foreach (int x in Play.PlaceOnGround(2, spot)) yield return x;
        if (_game.Zone.Devices.FirstOrDefault() is Device fab)
        {
            fab.Storage.AddAll(new Bill().Add("iron-ore", 60).Add("carbon-ore", 40).Add("copper-ore", 30));
            fab.Constructor.Order("base-core", 1);
            fab.Tick(3); // under way
        }
        foreach (int x in Play.OpenMenu(MenuTab.You)) yield return x;
        foreach (int x in Play.Order("fab", "trail-ration", 5)) yield return x;
        foreach (int x in _drive.DragWidget($"bag:{Play.BagSlotOf("biomass")}", "bar:4")) yield return x;
        var target = Enum.Parse<MenuTab>(_args.TryGetValue("tab", out string t) ? t : "you", true);
        if (target != MenuTab.You) foreach (int x in Play.OpenMenu(target)) yield return x;
        if (target == MenuTab.Tech) foreach (int x in _drive.ClickWidget($"tech:tab:{(_args.TryGetValue("techtab", out string tt) ? tt : "base")}")) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>A creature brought down with the tool and its body opened beside the bag.</summary>
    IEnumerable<int> Loot()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.34);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        var w = _game.Zone.Wildlife;
        Vector3 feet = _game.Survivor.WorldFeet;
        var prey = w.Sim.Creatures.Where(c => !c.Dead && !c.Species.Flies && c.Species.Plan != Wildlife.BodyPlan.Giant).OrderBy(c => w.FromPatch(c.Position).DistanceTo(feet)).First();
        foreach (int x in _drive.WalkTo(w.FromPatch(prey.Position), 12f, 60f)) yield return x;
        for (int i = 0; i < 40 && !prey.Dead; i++)
            foreach (int x in Play.ShootAt(() => w.FromPatch(prey.Rig.BoundsCenter))) yield return x;
        Lootable body = _game.Zone.Loot.All.FirstOrDefault(l => l.Creature == prey);
        if (body == null) yield break;
        foreach (int x in _drive.WalkTo(body.Position, 1.6f, 30f)) yield return x;
        foreach (int x in _drive.LookAt(body.Position + Vector3.Up * 0.3f, 6)) yield return x;
        foreach (int x in _drive.Tap(Key.E)) yield return x;
        foreach (int x in _drive.Wait(40)) yield return x;
    }

    /// <summary>A shot in flight from the tool's muzzle, beside the sparks and scorches of the two before it.</summary>
    IEnumerable<int> Bolt()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.3);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clouds);
        Vector3 feet = _game.Survivor.WorldFeet;
        Vector3 rock = _game.Zone.GroundAt(feet.X + 14f, feet.Z - 8f);
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in Play.ShootAt(() => rock)) yield return x;
        foreach (int x in Play.ShootAt(() => rock + new Vector3(1.2f, 0f, 0.6f))) yield return x;
        foreach (int x in _drive.LookAt(rock + new Vector3(-1f, 0.2f, 0f), 4)) yield return x;
        _game.Controls.Primary = true;
        yield return 0;
        _game.Controls.Primary = false;
        foreach (int x in _drive.Wait((int)Num("frames", 2))) yield return x;
    }

    /// <summary>Inside the bunker, a shot at its wall: the bolt, and the scorch on the wall.</summary>
    IEnumerable<int> BunkerShot()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.3);
        foreach (int x in _drive.WalkTo(_game.Bunker.At(new Vector3(-1.5f, 0.25f, -0.5f)), 0.4f, 15f)) yield return x;
        Vector3 wall = _game.Bunker.At(new Vector3(3.25f, 1.3f, -0.6f));
        foreach (int x in Play.ShootAt(() => wall)) yield return x;
        foreach (int x in Play.ShootAt(() => wall + new Vector3(0f, 0.25f, 0f))) yield return x;
        foreach (int x in _drive.LookAt(wall + new Vector3(0, 0.1f, 0), 4)) yield return x;
        _game.Controls.Primary = true;
        yield return 0;
        _game.Controls.Primary = false;
        foreach (int x in _drive.Wait(1)) yield return x;
    }

    /// <summary>Night, the head torch on, out by the bunker's stairs.</summary>
    IEnumerable<int> Torch()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.02);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Vector3 out1 = _game.Bunker.Ahead(6f, 3f);
        foreach (int x in _drive.WalkTo(out1, 1f, 30f)) yield return x;
        foreach (int x in _drive.Tap(Key.L)) yield return x;
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.LookAt(_game.Bunker.StairTop + Vector3.Up * 0.8f, 30)) yield return x;
    }

    /// <summary>Walks to the side of a block's face and places against it.</summary>
    IEnumerable<int> PlaceFrom(Vessel v, int slot, Int3 on, Dir face, float back)
    {
        Vector3 at = v.ToWorld(StructureBridge.CellCenter(v.Structure, on));
        Vector3 centre = v.Body.Position.ToGodot();
        Vector3 dir = face is Dir.NegY or Dir.PosY ? v.ToWorld(new N.Vector3(on.X, 0, on.Z)) - centre : v.ToWorld(new N.Vector3(face.Vec().X, 0, face.Vec().Z)) - centre;
        dir.Y = 0;
        Vector3 stand = at + (dir.LengthSquared() > 0.01f ? dir.Normalized() : Vector3.Back) * back;
        foreach (int x in _drive.WalkTo(new Vector3(stand.X, 0, stand.Z), 0.6f, 20f)) yield return x;
        foreach (int x in Play.PlaceBlock(slot, v, on, face)) yield return x;
    }

    /// <summary>A base built by hand: its core, a container, a generator, a small constructor, steel and glass, the survival constructor beside it.</summary>
    IEnumerable<int> BaseView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.36);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Mode = GameMode.Creative; // STAGING: free blocks, everything unlocked
        Hand(("base-core", 1), ("survival-constructor", 1));
        foreach (int x in Play.ToToolbar("base-core", 3)) yield return x;
        foreach (int x in Play.ToToolbar("survival-constructor", 2)) yield return x;
        foreach (var (b, s) in new[] { ("container", 4), ("generator", 5), ("smallfab", 6), ("steel", 7), ("glass", 8) })
            foreach (int x in Play.BlockToToolbar(b, false, s)) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
        Vector3 site = Play.FlatSpot(_game.Start, 24f, 1.0f, 0.6f);
        foreach (int x in Play.StandOff(site, _game.Start, 7f)) yield return x;
        foreach (int x in Play.PlaceOnGround(3, site)) yield return x;
        Vessel b0 = _game.Zone.Vessels.FirstOrDefault(v => v.IsBase);
        if (b0 == null) yield break;
        var steps = new (int slot, Int3 on, Dir face)[]
        {
            (7, Int3.Zero, Dir.NegX), (7, Int3.Zero, Dir.PosX), (7, new Int3(-1, 0, 0), Dir.NegX), (4, new Int3(1, 0, 0), Dir.PosX),
            (5, Int3.Zero, Dir.PosZ), (6, new Int3(-1, 0, 0), Dir.PosZ), (8, Int3.Zero, Dir.PosY), (7, new Int3(-2, 0, 0), Dir.PosY),
        };
        foreach (var (slot, on, face) in steps)
            foreach (int x in PlaceFrom(b0, slot, on, face, 5f)) yield return x;
        Vector3 core = b0.Body.Position.ToGodot();
        Vector3 fabAt = b0.ToWorld(new N.Vector3(0f, -1f, -6f));
        foreach (int x in _drive.WalkTo(b0.ToWorld(new N.Vector3(0f, -1f, -9f)), 1f, 20f)) yield return x;
        foreach (int x in Play.PlaceOnGround(2, _game.Zone.GroundAt(fabAt.X, fabAt.Z))) yield return x;
        foreach (int x in _drive.UntilSettled()) yield return x;
        float heading = (float)Num("heading", 210) * Mathf.Pi / 180f;
        Vector3 eye = core + new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading)) * 15f;
        eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + 5f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(core - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(20)) yield return x;
    }

    /// <summary>A small vessel built by hand on its core, lifting off.</summary>
    IEnumerable<int> VesselTakeoff()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.37);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Mode = GameMode.Creative; // STAGING: free blocks, everything unlocked
        Hand(("vessel-core", 1));
        foreach (int x in Play.ToToolbar("vessel-core", 3)) yield return x;
        var slots = new Dictionary<string, int> { ["cockpit"] = 4, ["generator"] = 5, ["fueltank"] = 6, ["gyro"] = 7, ["thruster"] = 8, ["steel"] = 1, ["gear"] = 2 };
        foreach (var (b, s) in slots) foreach (int x in Play.BlockToToolbar(b, true, s)) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
        Vector3 pad = Play.FlatSpot(_game.Start, 20f, 2.0f, 0.3f);
        foreach (int x in Play.StandOff(pad, _game.Start, 5f)) yield return x;
        foreach (int x in Play.PlaceOnGround(3, pad)) yield return x;
        Vessel v = _game.Zone.Vessels.FirstOrDefault(x => !x.IsBase && !x.IsRuin);
        if (v == null) yield break;
        foreach (int x in Play.BuildVessel(v, slots, _ => { })) yield return x;
        Vector3 ground = v.Body.Position.ToGodot();
        foreach (int x in _drive.Board(v)) yield return x;
        _game.Visor.Visible = false;
        _game.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait((int)Num("ticks", 70))) yield return x;
        Vector3 now = v.Body.Position.ToGodot();
        float heading = (float)Num("heading", 140) * Mathf.Pi / 180f;
        Vector3 eye = ground + new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading)) * 9f;
        eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + 1.7f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(now - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(4)) yield return x;
    }
}
