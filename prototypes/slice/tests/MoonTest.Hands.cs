using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Slice.Items;
using Slice.Player;
using Slice.Surface;
using Slice.Vessels;
using Wildlife;
using GalHack.Physics.Engine;
using S = GalHack.Structures;

namespace Slice.Tests;

/// <summary>Off the start world the hands work as on it: a weapon and the Sleeper tool in orbit and on the airless moon.</summary>
public partial class MoonTest
{
    /// <summary>Stopped in orbit, up on the Skiff's deck: a pistol's bolts and the tool's blast both wear someone else's hull beside it.</summary>
    IEnumerable<int> HandsInOrbit()
    {
        GD.Print("-- hands in orbit --");
        foreach (int x in _pilot.Stop()) yield return x;
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(_game.Survivor.Seated == null, "up out of the seat in orbit");
        _game.Controls.Hold(Key.Space); // off the deck and floating, clear of the Skiff's cabin
        foreach (int x in _drive.Wait(8)) yield return x;
        _game.Controls.ReleaseAll();
        foreach (int x in _drive.Wait(40)) yield return x;
        if (_game.Survivor.Seated == null) foreach (int x in HandsOnHull("orbit")) yield return x;
        foreach (int x in _pilot.Board(_skiff)) yield return x;
    }

    /// <summary>A raiders' row of hull set beside the sleeper: a pistol's bolts land on it as block hits, and the tool's blast wears it.</summary>
    IEnumerable<int> HandsOnHull(string where)
    {
        Vector3 look = _game.Survivor.WorldLook();
        Vessel row = TestRows.Row(_game, "raiders", false, MathF.Atan2(look.Z, look.X), out S.Int3[] cells, 6f);
        foreach (int x in _drive.Wait(20)) yield return x;
        Vector3 Face() => TestRows.FaceOf(_game, row, cells[1]);
        float Total() => row.Structure.Blocks().Sum(b => b.block.Hp);
        float was = Total();
        int blocks = _game.Arms.BlockHits;
        foreach (int x in Fire("tack-pistol", Face(), 4)) yield return x;
        float shot = Total();
        Check(_game.Arms.BlockHits > blocks && shot < was, $"in {where} a pistol's bolts land on a raiders' hull: {_game.Arms.BlockHits - blocks} block hits, {was - shot:F0} hp off ({_game.Arms.Fired} fired, {_game.Arms.Hits} hits, hand {_game.Pack.Hand}, the hull {row.Body.Position.ToGodot().DistanceTo(_game.Survivor.WorldEye()):F1} m off, the probe: {_game.Arms.Probe(_game.Survivor.WorldEye(), Face())})");
        foreach (int x in ToolIn(ToolMode.Gather)) yield return x;
        foreach (int x in _drive.LookAt(Face(), 6)) yield return x;
        foreach (int x in _drive.HoldPrimary(Face(), 120)) yield return x;
        Check(Total() < shot, $"and the Sleeper tool's blast wears it: {shot - Total():F1} hp off in 2 s");
        row.Zone.Remove(row);
    }

    /// <summary>On a moon's ground: the weapon and the tool on a hull, on an animal if the moon has any, and the tool digs the ground.</summary>
    IEnumerable<int> HandsOnGround(string where)
    {
        GD.Print($"-- hands on {where} --");
        SurfaceZone zone = _game.Zone;
        foreach (int x in HandsOnHull(where)) yield return x;
        zone.Wildlife.Frozen = true;
        Vector3 feet = _game.Survivor.WorldFeet, ahead = _game.Survivor.WorldLook() with { Y = 0f };
        ahead = ahead.LengthSquared() < 1e-4f ? Vector3.Forward : ahead.Normalized();
        Species kind = zone.Wildlife.Fauna?.Species?.FirstOrDefault(x => !x.Flies && x.Rig != RigKind.Swarm);
        if (kind != null && zone.Wildlife.Sim != null)
        {
            Creature c = zone.Wildlife.Sim.Spawn(kind, zone.Wildlife.PatchLocal(zone.GroundAt(feet.X + ahead.X * 6f, feet.Z + ahead.Z * 6f)));
            c.Health = 4000f;
            foreach (int x in _drive.Wait(10)) yield return x;
            Vector3 Mid() => zone.Wildlife.WorldOf(c) + Vector3.Up * 0.4f;
            foreach (int x in Fire("tack-pistol", Mid(), 4)) yield return x;
            float shot = c.Health;
            Check(shot < 4000f, $"on {where} a pistol's bolts hurt an animal: {4000f - shot:F0}");
            foreach (int x in ToolIn(ToolMode.Gather)) yield return x;
            foreach (int x in _drive.LookAt(Mid(), 6)) yield return x;
            foreach (int x in _drive.Click()) yield return x;
            Check(c.Health < shot, $"and the tool's arc hurts it: {shot - c.Health:F0}");
        }
        else GD.Print($"  ({where} has no wildlife to shoot)");
        foreach (int x in ToolIn(ToolMode.Gather)) yield return x;
        Vector3 ground = zone.GroundAt(feet.X - ahead.X * 2f, feet.Z - ahead.Z * 2f); // behind: clear of the hull and the animal
        int gathered = _game.Tool.Gathered.Values.Sum();
        foreach (int x in _drive.LookAt(ground, 10)) yield return x;
        foreach (int x in _drive.HoldPrimary(ground, 180)) yield return x;
        Check(_game.Tool.Gathered.Values.Sum() > gathered, $"and the tool gathers the ground: +{_game.Tool.Gathered.Values.Sum() - gathered} ({string.Join(", ", _game.Tool.Gathered.Select(kv => $"{kv.Key} {kv.Value}"))})");
    }

    /// <summary>A weapon armed and fired a few times at a point.</summary>
    IEnumerable<int> Fire(string weapon, Vector3 at, int shots)
    {
        WeaponTest.Arm(_game, weapon);
        foreach (int x in _drive.Wait(4)) yield return x;
        foreach (int x in _drive.LookAt(at, 8)) yield return x;
        for (int i = 0; i < shots; i++)
        {
            foreach (int x in _drive.Click()) yield return x;
            foreach (int x in _drive.Wait(14)) yield return x;
        }
        foreach (int x in _drive.Wait(20)) yield return x;
    }

    /// <summary>The Sleeper tool in hand, in a mode.</summary>
    IEnumerable<int> ToolIn(ToolMode mode)
    {
        var pack = _game.Pack;
        if (!pack.Has("sleeper-tool")) pack.Add("sleeper-tool", 1);
        int slot = Enumerable.Range(0, PlayerInventory.ToolbarSlots).FirstOrDefault(i => pack.Grid[i].Item == "sleeper-tool");
        if (pack.Grid[slot].Item != "sleeper-tool") { pack.Grid[slot].Item = "sleeper-tool"; pack.Grid[slot].Count = 1; pack.Grid.Reconcile(); }
        pack.Selected = slot;
        foreach (int x in _drive.Wait(2)) yield return x;
        for (int i = 0; i < 3 && _game.Tool.Mode != mode; i++) foreach (int x in _drive.Tap(Key.Q)) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
    }
}
