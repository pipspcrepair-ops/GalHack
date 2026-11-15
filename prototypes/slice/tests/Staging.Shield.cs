using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Vessels;
using N = System.Numerics;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// --view=shield: <see cref="BarTest.Rig"/>'s vessel with a shield generator on its floor, by day on real ground, seen low from
/// beside it, its shield struck by two rounds on its lowest blocks facing you: a moment after the second (1-ripple.png), and as
/// they fade (2-fading.png). Grounded, the shield is a dome: nothing of it shows in the ground.
/// --dir=DIR is the folder.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> ShieldView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.45);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Sky.Paused = true;
        string dir = _args.GetValueOrDefault("dir", "shield");
        Directory.CreateDirectory(dir);
        Vector3 site = Play.FlatSpot(_game.Survivor.WorldFeet, 40f, span: 34f);
        Fell(site, 26f);
        _game.Survivor.PlaceOutside((site + new Vector3(14f, 2f, 10f)).ToNumerics());
        foreach (int x in _drive.Wait(20)) yield return x;
        Vessel v = BarTest.Rig(_game, site, 0f, out _);
        Structure s = v.Structure;
        BlockType shield = s.Catalogue.Type("shield-generator");
        foreach (Int3 cell in from x in Enumerable.Range(-3, 7) from z in Enumerable.Range(-3, 7) select new Int3(x, 1, z))
            if (Building.Place(s, cell, Building.Make(s, shield)) == PlaceError.None) break;
        v.ApplyEdits();
        v.Storage.Add("fold-crystal", 4);
        foreach (int x in _drive.Wait(60 * 10)) yield return x; // the shield fills
        GD.Print($"  (shield {v.Power.ShieldHp:F0} of {v.Power.ShieldMaxHp:F0})");

        // Low, from beside it: the hits land on its lowest blocks on that side, where the dome is cut at its underside.
        var (lo, hi) = s.Bounds();
        Vector3 centre = v.ToWorld(new N.Vector3(lo.X + hi.X, lo.Y + hi.Y, lo.Z + hi.Z) * 0.5f * s.Grid.CellSize);
        Vector3 stand = v.ToWorld(new N.Vector3(hi.X + 7f, lo.Y, hi.Z + 5f) * s.Grid.CellSize);
        foreach (int x in StandAndLook(stand, centre)) yield return x;
        foreach (Int3 cell in s.Blocks().Select(b => b.pos).Where(p => p.Y == lo.Y)
                     .OrderBy(p => v.ToWorld(StructureBridge.CellCenter(s, p)).DistanceTo(stand)).Take(2))
        {
            v.Hits.Shot(cell, v.ToWorld(StructureBridge.CellCenter(s, cell)).ToNumerics(), N.Vector3.UnitX, 160f, DamageType.Kinetic);
            foreach (int x in _drive.Wait((int)Num("gap", 12))) yield return x;
        }
        foreach (int x in _drive.Wait((int)Num("after", 4))) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "1-ripple.png"))) yield return x;
        foreach (int x in _drive.Wait(18)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "2-fading.png"))) yield return x;
    }
}
