using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using Godot;
using Slice.Items;
using Slice.Ui;
using Slice.Vessels;
using N = System.Numerics;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>The views of blocks as items: crafting them, carrying them, the shape picker, a big storage filtered.</summary>
public sealed partial class Staging
{
    /// <summary>A constructor's panel crafting blocks: steel and a container ordered, their sub-parts under them, the list on BLOCK, a tooltip up.</summary>
    IEnumerable<int> ConstructorBlocks()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.33);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Hand(("survival-constructor", 1));
        foreach (int x in Play.ToToolbar("survival-constructor", 2)) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
        foreach (int x in _drive.WalkTo(_game.Bunker.Ahead(5f), 1f, 20f)) yield return x;
        Vector3 spot = _game.Zone.GroundAt(_game.Bunker.Ahead(9f, 2f).X, _game.Bunker.Ahead(9f, 2f).Z);
        foreach (int x in Play.PlaceOnGround(2, spot)) yield return x;
        Device fab = _game.Zone.Devices.FirstOrDefault();
        if (fab == null) yield break;
        fab.Storage.AddAll(new Bill().Add("iron-ore", 160).Add("carbon-ore", 120).Add("copper-ore", 60).Add("silicon-ore", 20).Add(Play.BlockItem("steel"), 6));
        foreach (int x in Play.OpenMenu(MenuTab.Core)) yield return x;
        foreach (int x in Play.Order("core", Play.BlockItem("container"), 1)) yield return x;
        foreach (int x in Play.Order("core", Play.BlockItem("steel"), 5)) yield return x;
        foreach (int x in _drive.ClickWidget("recipes:core:cat:all")) yield return x;
        foreach (int x in _drive.ClickWidget("recipes:core:cat:blocks")) yield return x;
        fab.Tick(2.5);
        foreach (int x in _drive.Wait(4)) yield return x;
        if (_game.Menu.RectOf($"order:core:{Play.BlockItem("steel")}:1") is Rect2 r)
            _game.Controls.Mouse = r.Position + new Vector2(-200, 12);
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>The owner's angle: standing at a base's small constructor, looking at its screen, with markers about and the use prompt up.</summary>
    IEnumerable<int> MachineScreenView()
    {
        foreach (int x in BaseView(survival: true)) yield return x;
        Vessel b0 = _game.Zone.Vessels.FirstOrDefault(v => v.IsBase && !v.IsRuin);
        if (b0 == null) yield break;
        _game.FixedCamera = null;
        _game.MachineScreens.Sync();
        foreach (int x in _drive.Wait(10)) yield return x;
        var m = _game.MachineScreens.Screens.FirstOrDefault(e => e.Key.owner == b0 && b0.Machines.ContainsKey(e.Key.cell)).Value;
        if (m == null) yield break;
        Vector3 normal = m.Screen.GlobalBasis.Z.Normalized();
        Vector3 face = m.Screen.GlobalPosition;
        foreach (int x in StandAndLook(new Vector3(face.X, 0f, face.Z) + normal * 1.3f, face, 20)) yield return x;
        Vector3 eye = _game.Camera.GlobalPosition;
        Vector3 flat = new Vector3(-normal.X, 0f, -normal.Z).Normalized();
        for (int k = -2; k <= 2; k++)
        {
            Vector3 w = eye + flat.Rotated(Vector3.Up, k * 0.1f) * (35f + 5f * System.Math.Abs(k));
            _game.Spaceflight.Charts.Mark(w.X, w.Z);
        }
        _game.Scan();
        foreach (int x in _drive.Wait(150)) yield return x;
        GD.Print($"(the screen at {face}, facing {normal}; the prompt \"{_game.UsePrompt()}\"; {_game.Visor.Drawn.Count} markers up, {_game.Visor.ScreenBoxes(new Vector2(1920, 1080), 1f).Count} screen boxes)");
    }

    /// <summary>The visor with a block family in hand: a long name, the count from the chosen source, the shape picker.</summary>
    IEnumerable<int> HandView()
    {
        foreach (int x in BaseView(survival: true)) yield return x;
        Vessel b0 = _game.Zone.Vessels.FirstOrDefault(v => v.IsBase && !v.IsRuin);
        if (b0 == null) yield break;
        _game.FixedCamera = null;
        string hardened = Play.BlockItem("hardened");
        Hand((hardened, 12));
        b0.Storage.Add(hardened, 340);
        foreach (int x in Play.ToToolbar(hardened, 1)) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
        foreach (int x in Play.Select(1)) yield return x;
        _game.Builder.ShapeIndex = 3;
        foreach (int x in _drive.WalkTo(b0.ToWorld(new N.Vector3(0, 0, -9f)), 1f, 20f)) yield return x;
        foreach (int x in _drive.LookAt(b0.ToWorld(new N.Vector3(0, 0, -1f)), 12)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>A big base storage on the CORE tab, filtered by text, a stack's tooltip up.</summary>
    IEnumerable<int> StorageFilter()
    {
        foreach (int x in BaseView(survival: true)) yield return x;
        Vessel b0 = _game.Zone.Vessels.FirstOrDefault(v => v.IsBase && !v.IsRuin);
        if (b0 == null) yield break;
        _game.FixedCamera = null;
        b0.Storage.CapacitySu = 2_000_000; // STAGING: a very big base
        var rng = new Random(7);
        foreach (ItemDef i in _game.Zone.Data.Items.Items) b0.Storage.Add(i.Id, 1 + rng.Next(i.StackSize * 3));
        foreach (int x in Play.OpenMenu(MenuTab.Core)) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        int pick = _game.Menu.Root.Core.Near().IndexOf(b0);
        if (pick >= 0) foreach (int x in _drive.ClickWidget($"core:pick:{pick}")) yield return x;
        foreach (int x in _drive.ClickWidget("storage:filter")) yield return x;
        _game.Controls.Type(_args.TryGetValue("filter", out string f) ? f : "steel");
        foreach (int x in _drive.Wait(3)) yield return x;
        foreach (int x in _drive.ClickWidget("storage:sort")) yield return x; // by name
        foreach (int x in _drive.Wait(3)) yield return x;
        IReadOnlyList<int> shown = ItemViews.Get("storage").Visible(b0.StorageGrid);
        if (shown.Count > 0 && _game.Menu.RectOf($"store:{shown[Math.Min(5, shown.Count - 1)]}") is Rect2 r) _game.Controls.Mouse = r.GetCenter();
        foreach (int x in _drive.Wait(30)) yield return x;
    }
}

