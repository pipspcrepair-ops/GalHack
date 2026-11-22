using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Infra;
using Slice.Player;

namespace Slice.Tests;

/// <summary>
/// Power poles and pylons felled as trees are (--view=power): the nearest line pole seen down its line, shot through at its
/// foot until it goes over, and seen again down; then the nearest pylon (rockets at its foot) where the world has one. The
/// run fails unless each went over (falling as a tree does, and lying flat), its drawing went, and every wire it held now
/// hangs from the pole at its other end, none left strung to it.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> PowerView()
    {
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        GD.Print($"world: seed {_game.World.Seed}, {_game.World}");
        PowerLines lines = _game.Zone.Planet.Infra.Power;
        GD.Print($"(power: {lines.Poles.Count} poles, {lines.Poles.Count(q => q.Pylon)} pylons, {lines.Poles.Count(q => q.Fallen)} down, {lines.Wires.Count} wires; roles {string.Join(", ", lines.Poles.GroupBy(q => q.Role).Select(g => $"{g.Key} {g.Count()}"))})");
        Vector3 from = _game.Survivor.WorldFeet;
        Vector3 At(int i) => _game.Zone.Planet.NearestCopy(new Vector3((float)lines.Poles[i].X, lines.Poles[i].Ground, (float)lines.Poles[i].Z), from);
        foreach (bool pylon in new[] { false, true })
        {
            int pole = Enumerable.Range(0, lines.Poles.Count).Where(i => lines.Poles[i].Role == PowerRole.Line && !lines.Poles[i].Fallen && lines.Poles[i].Pylon == pylon)
                .OrderBy(i => At(i).DistanceTo(from)).DefaultIfEmpty(-1).First();
            string what = pylon ? "pylon" : "pole";
            if (pole < 0) { GD.Print($"(power: no standing {what} on this world)"); continue; }
            PowerPole p = lines.Poles[pole];
            Vector3 foot = At(pole), side = new(p.SideX, 0f, p.SideZ), along = new(-p.SideZ, 0f, p.SideX);
            float back = pylon ? 80f : 26f;
            Vector3 eye = foot + along * back + side * back * 0.3f;
            eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + 1.7f;
            Vector3 look = foot - along * back * 0.6f + Vector3.Up * p.Height * 0.5f;
            foreach (int x in Shot($"{what}-before", eye, look)) yield return x;
            int felled = _game.Zone.PolesFelled, shots = 0;
            var held = Enumerable.Range(0, lines.Wires.Count).Where(i => lines.Wires[i].Spanned && (lines.Wires[i].A == pole || lines.Wires[i].B == pole)).ToList();
            GalHack.Arms.WeaponDef gun = pylon ? WorldFire.Rocket(_game) : _game.Zone.Data.Arms.OfItem(Wildlife.Weapons.ItemOf("rifle"));
            for (; shots < 120 && _game.Zone.PolesFelled == felled; shots++)
            {
                Vector3 aim = foot + Vector3.Up * 1f, muzzle = aim + side * 8f + Vector3.Up * 0.5f;
                WorldFire.Fire(_game, gun, muzzle, aim);
                foreach (int x in _drive.Wait(pylon ? 20 : 5)) yield return x;
            }
            foreach (int x in _drive.Wait(150)) yield return x; // its fall, a tree's
            float lean = _game.Zone.Trees.GetChildren().OfType<FallingTree>().Select(t => t.Basis.Y.AngleTo(Vector3.Up)).DefaultIfEmpty(0f).Max();
            bool drawn = _game.Zone.Infra.GetChildren().Any(t => t.GetNodeOrNull<Node3D>(InfraLayer.PoleNode(pole)) is { Visible: true });
            int strung = held.Count(i => lines.Wires[i].Spanned), hanging = held.Count(i => !lines.Wires[i].Spanned && lines.Wires[i].A != pole);
            bool ok = p.Fallen && lean > 1.3f && !drawn && strung == 0 && hanging == held.Count;
            if (!ok) Faults++;
            GD.Print($"  {(ok ? "ok   " : "FAIL ")} the {what} went over after {shots} shots at its foot (fallen {p.Fallen}, lying {Mathf.RadToDeg(lean):F0} degrees over, its drawing {(drawn ? "still standing" : "gone")}); of its {held.Count} wires {hanging} hang from the pole at their other end, {strung} still strung to it");
            foreach (int x in Shot($"{what}-after", eye, look)) yield return x;
            from = _game.Survivor.WorldFeet;
        }
        _game.FixedCamera = null;

        IEnumerable<int> Shot(string tag, Vector3 eye, Vector3 look)
        {
            _game.Survivor.PlaceOutside((eye - Vector3.Up * 1.7f).ToNumerics());
            for (int i = 0; i < 900; i++)
            {
                _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
                _game.Zone.Sky.TimeOfDay = HighSun(_game);
                yield return 0;
                if (i > 240 && _game.Zone.Streamer.Settled) break;
            }
            Snap(tag);
        }
    }
}
