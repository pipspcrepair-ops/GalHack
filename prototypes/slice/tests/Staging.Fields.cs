using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Vessels;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Proof frames of force fields (--view=fields, --dir=folder): one drawn across the field room's
/// opening by the sleeper's own hands, red while it can't go and blue when it can (the sleeper's
/// view); placed, with its four L-shaped emitters, then two of them plated; the room's outer door
/// open with its own field; the field knocked down, its emitters standing, and grown back.
/// </summary>
public partial class Staging
{
    IEnumerable<int> FieldViews()
    {
        string dir = _args.GetValueOrDefault("dir", "user://fields");
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        FieldRig rig = FieldRig.Build(_game, FlatSpot(_game.Survivor.WorldFeet, 30f));
        Vessel v = rig.Room;
        Structure s = v.Structure;
        var c = _game.Controls;
        const string T1 = "force-field-t1";
        _game.Pack.Add(T1, 1);
        int slot = Enumerable.Range(0, Slice.Items.PlayerInventory.ToolbarSlots).FirstOrDefault(i => _game.Pack.Grid[i].Item == T1, -1);
        if (slot < 0)
        {
            slot = 2;
            foreach (int x in Play.ToToolbar(T1, slot)) yield return x;
            foreach (int x in Play.CloseMenu()) yield return x;
        }
        foreach (int x in Play.Select(slot)) yield return x;
        Vector3 stand = rig.World(4.5f, 0f, -6.5f);
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(stand.X, stand.Z) + Vector3.Up * 0.1f).ToNumerics());
        foreach (int x in _drive.UntilSettled(20)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;

        // Drawn by hand: pressed on the frame's inside face, dragged to the middle (red), then to the far corner (blue).
        Vector3 frame = rig.World(1.5f, 1f, 0f);
        foreach (int x in _drive.LookAt(frame, 8)) yield return x;
        c.PrimaryPressed = c.Primary = true;
        _drive.Face(frame);
        yield return 0;
        IEnumerable<int> Hold(Vector3 to, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                c.Primary = true;
                _drive.Face(to);
                yield return 0;
            }
        }
        foreach (int x in Hold(rig.World(4f, 2f, 0f), 20)) yield return x;
        foreach (int x in Shot($"{dir}/fields-drag-red.png")) yield return x;
        foreach (int x in Hold(rig.World(5f, 3f, 0f), 20)) yield return x;
        foreach (int x in Shot($"{dir}/fields-drag-blue.png")) yield return x;
        c.Primary = false;
        foreach (int x in _drive.Wait(4)) yield return x;
        ForceField f = s.Fields.FirstOrDefault();
        if (f == null) { GD.PrintErr("fields: no field placed"); yield break; }

        // Placed, from out in front and to one side; then the two lower emitters plated (iron, combat) under the two bare ones, seen close.
        Vector3 behind = rig.World(-6f, 0f, -12f);
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(behind.X, behind.Z) + Vector3.Up * 0.1f).ToNumerics());
        _game.Visor.Visible = false;
        IEnumerable<int> Frame(Vector3 eye, Vector3 at, string file)
        {
            _game.FixedCamera = new Transform3D(Basis.LookingAt((at - eye).Normalized(), Vector3.Up), eye);
            foreach (int x in _drive.Wait(20)) yield return x;
            foreach (int x in Shot($"{dir}/{file}")) yield return x;
        }
        Vector3 front = rig.World(0f, 2.8f, -6f), opening = rig.World(3.5f, 2f, 0f);
        foreach (int x in Frame(front, opening, "fields-placed.png")) yield return x;
        foreach (var (corner, plating) in new[] { (0, "iron-plated"), (1, "combat-plated") })
            Building.Plate(s, f.Corner(corner), s.TypeOf(s.Get(f.Corner(corner))).Platings.First(p => p.Plate == s.Catalogue.Type(plating).Plate));
        v.ApplyEdits();
        foreach (int x in Frame(rig.World(3.5f, 2.2f, -4.2f), rig.World(3.5f, 1.9f, 0f), "fields-plated.png")) yield return x;

        // The outer door open, powered: its own field across it.
        Func<Vector3> feet = _game.Doors.Feet;
        Vector3 inDoor = rig.World(FieldRig.Door.X, FieldRig.Door.Y, FieldRig.Door.Z) - Vector3.Up * 0.9f;
        _game.Doors.Feet = () => inDoor;
        _game.Doors.Use(v, FieldRig.Door);
        Vector3 side = rig.World(16f, 0f, 2f);
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(side.X, side.Z) + Vector3.Up * 0.1f).ToNumerics());
        foreach (int x in _drive.Wait(40)) yield return x;
        foreach (int x in Frame(rig.World(11.5f, 2.2f, 0.8f), rig.World(7f, 1.5f, 2f), "fields-door.png")) yield return x;
        _game.Doors.Feet = feet;
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(behind.X, behind.Z) + Vector3.Up * 0.1f).ToNumerics());

        // Knocked to nothing, its emitters standing; then grown back after its wait.
        ForceFields.Hit(v, f, f.Strength + 1f);
        foreach (int x in Frame(front, opening, "fields-down.png")) yield return x;
        _game.Fields.TimeScale = 20f; // STAGING: its wait and regrowth at twenty times play speed
        for (int i = 0; i < 60 * 30 && f.Strength < (_game.Zone.Data.Rules.ForceFields.TierOf(f.Tier)?.Strength ?? 0f); i++) yield return 0;
        _game.Fields.TimeScale = 1f;
        foreach (int x in Frame(front, opening, "fields-back.png")) yield return x;
        _game.FixedCamera = null;
        _game.Visor.Visible = true;
    }
}
