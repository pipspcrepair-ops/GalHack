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
/// --view=shield: <see cref="BarTest.Rig"/>'s vessel with a shield generator on its floor, by day on real ground, its shield
/// struck by two rounds on the side facing you: a moment after the second (1-ripple.png), and as they fade (2-fading.png).
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

        // From off its starboard quarter, a little above: the hits land on the side facing the camera.
        var (lo, hi) = s.Bounds();
        Vector3 centre = v.ToWorld(new N.Vector3(lo.X + hi.X, lo.Y + hi.Y, lo.Z + hi.Z) * 0.5f * s.Grid.CellSize);
        Vector3 eye = v.ToWorld(new N.Vector3(hi.X + 11f, hi.Y + 4f, hi.Z + 7f) * s.Grid.CellSize);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(centre - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(10)) yield return x;
        Int3[] struck = s.Blocks().Select(b => b.pos).Where(p => p.X == hi.X).OrderBy(p => p.Y).ThenBy(p => -p.Z).Take(1)
            .Concat(s.Blocks().Select(b => b.pos).Where(p => p.Z == hi.Z).OrderByDescending(p => p.Y).Take(1)).ToArray();
        foreach (Int3 cell in struck)
        {
            N.Vector3 at = StructureBridge.CellCenter(s, cell);
            v.Hits.Shot(cell, v.ToWorld(at).ToNumerics(), N.Vector3.UnitX, 160f, DamageType.Kinetic);
            foreach (int x in _drive.Wait((int)Num("gap", 12))) yield return x;
        }
        foreach (int x in _drive.Wait((int)Num("after", 4))) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "1-ripple.png"))) yield return x;
        foreach (int x in _drive.Wait(18)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "2-fading.png"))) yield return x;
        _game.FixedCamera = null;
    }
}
