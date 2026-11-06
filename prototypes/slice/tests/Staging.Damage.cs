using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>The damage views: a crashed vessel, a hull under fire, a wall at every stage, a repair.</summary>
public sealed partial class Staging
{
    IEnumerable<int> DamageSetting()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.38);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>A clear spot on the ground, some way ahead of the castaway.</summary>
    N.Vector3 DamageSite(float ahead)
    {
        Vector3 p = _game.Survivor.WorldFeet + new Vector3(ahead, 0f, 30f);
        Vector3 g = _game.Zone.GroundAt(p.X, p.Z);
        return new N.Vector3(g.X, g.Y, g.Z);
    }

    void Aim(Vector3 eye, Vector3 at) => _game.FixedCamera = new Transform3D(Basis.LookingAt(at - eye, Vector3.Up), eye);

    /// <summary>A small vessel flown into the ground for real, every stage of damage across it, and a smoking device.</summary>
    IEnumerable<int> DamageCrash()
    {
        foreach (int x in DamageSetting()) yield return x;
        Structure s = _game.Zone.Data.Blueprint("starter.gbp");
        s.Name = "Crashed";
        N.Vector3 site = DamageSite(14f);
        Vessel v = Vessel.Spawn(_game.Zone, s, site + new N.Vector3(0f, 9f, 0f), 0.5f, landed: false);
        v.Body.Velocity = new N.Vector3(4f, -(float)Num("speed", 38), 3f);
        foreach (int x in _drive.Wait(90)) yield return x;
        Vector3 at = v.Body.Position.ToGodot();
        Aim(at + new Vector3(4.5f, 2.6f, -5.5f), at + Vector3.Down * 0.2f);
        foreach (int x in _drive.Wait(4)) yield return x;
    }

    /// <summary>Gunfire raked along a hull: worn patches and impact marks.</summary>
    IEnumerable<int> DamageGunfire()
    {
        foreach (int x in DamageSetting()) yield return x;
        Structure s = _game.Zone.Data.Blueprint("starter.gbp");
        s.Name = "Target";
        Vessel v = Vessel.Spawn(_game.Zone, s, DamageSite(14f), 0.5f, landed: true);
        foreach (int x in _drive.Wait(4)) yield return x;

        // Each bolt really cast at the hull from the side, as a gunner would, landing where it lands.
        var rng = new Random(3);
        Vector3 at = v.Body.Position.ToGodot() + Vector3.Up * 1.2f;
        Vector3 from = at + new Vector3(6f, 0.8f, -6f);
        for (int volley = 0; volley < 40; volley++)
        {
            for (int bolt = 0; bolt < 3; bolt++)
            {
                Vector3 aim = at + new Vector3((float)rng.NextDouble() * 3f - 1.5f, (float)rng.NextDouble() * 1.6f - 0.8f, (float)rng.NextDouble() * 3f - 1.5f);
                Vector3 dir = (aim - from).Normalized();
                if (Slice.Game.WorldRay.Cast(_game.Zone, null, from, dir, 20f, GalHack.Physics.Layers.Hull, out Slice.Game.LookHit hit) && hit.OnBlock)
                    hit.Vessel.Hits.Shot(hit.Cell, hit.Point.ToNumerics(), hit.Normal.ToNumerics(), 18f, DamageType.Energy);
            }
            yield return 0;
        }
        foreach (int x in _drive.Wait(3)) yield return x;
        Aim(from + new Vector3(-1.6f, -0.2f, 1.6f), at);
        foreach (int x in _drive.Wait(4)) yield return x;
    }

    /// <summary>A base wall: one row per material, one column per stage of damage, whole on the left, destroyed on the right.</summary>
    IEnumerable<int> DamageWall()
    {
        foreach (int x in DamageSetting()) yield return x;
        var cat = _game.Zone.Data.Catalogue;
        var s = new Structure(cat, cat.Grid(GridSize.Large)) { Name = "Stages" };
        string[] rows = { "precursor", "glass", "concrete", "steel" };
        float[] health = { 1f, 0.7f, 0.4f, 0.1f };
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < health.Length; x++)
            {
                Block b = Building.Make(s, cat.Type(rows[y]));
                b.Hp = s.MaxHp(b) * health[x];
                s.Set(new Int3(x, y, 0), b);
            }
        s.DirtyChunks.Clear();
        N.Vector3 site = DamageSite(14f) + new N.Vector3(0f, 1.5f, 0f);
        Vessel v = Vessel.SpawnBase(_game.Zone, s, site, 0f);
        foreach (int x in _drive.Wait(4)) yield return x;
        Vector3 mid = v.ToWorld(new N.Vector3(3f, 3f, 0f));
        Aim(mid + new Vector3(0f, 0.6f, -9.5f), mid);
        foreach (int x in _drive.Wait(4)) yield return x;
    }

    /// <summary>A damaged block and the same block repaired beside it, paid for from the structure's storage.</summary>
    IEnumerable<int> DamageRepair()
    {
        foreach (int x in DamageSetting()) yield return x;
        var cat = _game.Zone.Data.Catalogue;
        var s = new Structure(cat, cat.Grid(GridSize.Large)) { Name = "Mended" };
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++) s.Set(new Int3(x, y, 0), Building.Make(s, cat.Type("steel")));
        s.DirtyChunks.Clear();
        Vessel v = Vessel.SpawnBase(_game.Zone, s, DamageSite(14f) + new N.Vector3(0f, 1.5f, 0f), 0f);
        foreach (int x in _drive.Wait(2)) yield return x;
        foreach (var (p, b) in s.Blocks().ToList()) s.SetHp(p, s.MaxHp(b) * 0.1f);
        v.View.Queue(s.DirtyChunks);
        s.DirtyChunks.Clear();
        foreach (int x in _drive.Wait(2)) yield return x;

        // The right-hand column repaired, by the game's own rule: paid for, step by step.
        v.Storage.CapacitySu = 1000;
        foreach (Int3 cell in new[] { new Int3(1, 0, 0), new Int3(1, 1, 0) })
        {
            foreach (var (id, n) in v.Hits.RepairBill(cell)) v.Storage.Add(id, n);
            v.Hits.Repair(cell);
        }
        foreach (int x in _drive.Wait(4)) yield return x;
        Vector3 mid = v.ToWorld(new N.Vector3(1f, 1f, 0f));
        Aim(mid + new Vector3(0f, 0.4f, -6f), mid);
        foreach (int x in _drive.Wait(4)) yield return x;
    }
}
