using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using Godot;
using Slice.Game;
using Slice.Items;
using Slice.Player;
using Slice.Surface;
using Slice.Vessels;
using Wildlife;

namespace Slice.Tests;

/// <summary>Off the start world the hands work as on it: a weapon and the Sleeper tool in orbit and on the airless moon.</summary>
public partial class MoonTest
{
    /// <summary>Stopped in orbit, up out of the seat: a pistol's bolts hurt the Skiff's floor and the tool's repair mends it.</summary>
    IEnumerable<int> HandsInOrbit()
    {
        GD.Print("-- hands in orbit --");
        Vessel v = _skiff;
        foreach (int x in _pilot.Stop()) yield return x;
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        var s = _game.Survivor;
        Vector3 look = s.WorldLook() with { Y = 0f };
        Vector3 floor = s.WorldFeet + (look.LengthSquared() > 1e-4f ? look.Normalized() : Vector3.Forward) * 1.5f + Vector3.Down * 0.4f;
        foreach (int x in _drive.LookAt(floor, 8)) yield return x;
        LookHit on = default;
        bool up = s.Seated == null && WorldRay.Cast(_game.Zone, s, 4f, Layers.Hull, out on) && on.Vessel == v;
        Check(up, $"up out of the seat in orbit, the Skiff's floor in the crosshair (seated {s.Seated != null})");
        if (!up)
        {
            foreach (int x in _pilot.Board(v)) yield return x;
            yield break;
        }
        float hp = v.Structure.Get(on.Cell).Hp;
        int blocks = _game.Arms.BlockHits;
        foreach (int x in Fire("tack-pistol", floor, 3)) yield return x;
        float shot = v.Structure.Has(on.Cell) ? v.Structure.Get(on.Cell).Hp : 0f;
        Check(_game.Arms.BlockHits > blocks && shot < hp, $"in orbit a pistol's bolts land: {_game.Arms.BlockHits - blocks} block hits, the floor {hp:F0} to {shot:F0} hp");
        if (v.Structure.Has(on.Cell))
        {
            foreach (var (id, n) in v.Hits.RepairBill(on.Cell)) _game.Pack.Add(id, n);
            foreach (int x in ToolIn(ToolMode.Repair)) yield return x;
            foreach (int x in _drive.HoldPrimary(floor, 120)) yield return x;
            Check(v.Structure.Get(on.Cell).Hp > shot, $"and the tool's repair mends it: {shot:F0} to {v.Structure.Get(on.Cell).Hp:F0} hp");
        }
        foreach (int x in _pilot.Board(v)) yield return x;
    }

    /// <summary>On the moon's ground: a pistol hurts a person, the tool zaps another and digs the ground for stone.</summary>
    IEnumerable<int> HandsOnGround(string where)
    {
        GD.Print($"-- hands on {where} --");
        SurfaceZone zone = _game.Zone;
        zone.Wildlife.Frozen = true;
        Creature shot = Bystander(6f);
        foreach (int x in _drive.Wait(10)) yield return x;
        float health = shot.Health;
        foreach (int x in Fire("tack-pistol", Chest(shot), 4)) yield return x;
        Check(shot.Dead || shot.Health < health, $"on {where} a pistol's bolts land: a person {health:F0} to {shot.Health:F0}");
        Creature zapped = Bystander(4f);
        foreach (int x in _drive.Wait(10)) yield return x;
        health = zapped.Health;
        int zaps = _game.Tool.Zapped;
        foreach (int x in ToolIn(ToolMode.Gather)) yield return x;
        foreach (int x in _drive.LookAt(Chest(zapped), 6)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        Check(_game.Tool.Zapped > zaps && zapped.Health < health, $"the tool's arc zaps a person: {health:F0} to {zapped.Health:F0}");
        Vector3 feet = _game.Survivor.WorldFeet, away = (feet - zone.Wildlife.WorldOf(zapped)) with { Y = 0f };
        Vector3 ground = zone.GroundAt(feet.X + away.Normalized().X * 2f, feet.Z + away.Normalized().Z * 2f);
        int gathered = _game.Tool.Gathered.Values.Sum();
        foreach (int x in _drive.LookAt(ground, 6)) yield return x;
        foreach (int x in _drive.HoldPrimary(ground, 180)) yield return x;
        Check(_game.Tool.Gathered.Values.Sum() > gathered, $"and it gathers the ground: +{_game.Tool.Gathered.Values.Sum() - gathered} ({string.Join(", ", _game.Tool.Gathered.Select(kv => $"{kv.Key} {kv.Value}"))})");
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

    Creature Bystander(float far) => SleeperToolTest.Bystander(_game, far, (ulong)System.Random.Shared.NextInt64());
    Vector3 Chest(Creature c) => SleeperToolTest.Chest(_game, c);
}
