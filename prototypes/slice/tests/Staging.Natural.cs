using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Infra;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Surface;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The natural-look proof: one run on one world takes every shot, in the game itself. A ruin with the
/// trees round it, ore tells in the open, the field they lie in, a boulder, and the chips off rock and
/// ore. --stem=PATH names the files (PATH-ruin.png and so on).
/// </summary>
public sealed partial class Staging
{
    void NatShot(string name)
    {
        string stem = _args.GetValueOrDefault("stem", "nat");
        string path = $"{stem}-{name}.png";
        Error e = _game.GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"NAT {path}: {e}");
    }

    /// <summary>Trees the place grew, in the generator's terms.</summary>
    int Grown(PoiSite s) => _game.Zone.Planet.Palette.Planted?.Values.Count(t => Mathf.Abs((float)t.X - s.Origin.X) < 96f && Mathf.Abs((float)t.Z - s.Origin.Z) < 96f && OnSite(s, t)) ?? 0;

    static bool OnSite(PoiSite s, PlanetSurface.Terrain.PlantedFlora t)
    {
        var c = new Int3((int)t.X - s.Origin.X, 0, (int)t.Z - s.Origin.Z);
        return s.Instance.Growth.Any(g => g.Cell.X == c.X && g.Cell.Z == c.Z);
    }

    Vector3 Feet(Vector3 at) => at with { Y = _game.Zone.GroundAt(at.X, at.Z).Y + 0.2f };

    IEnumerable<int> Natural()
    {
        var planet = _game.Zone.Planet;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.38);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        GD.Print($"NAT world {_game.World}");
        Vector3 start = _game.Survivor.WorldFeet;

        // A ruin that grew trees, nearest first; the camera lifted a little, far enough back to take in the land round it.
        PoiSite site = planet.Pois.Where(s => Grown(s) > 0).OrderBy(s => _game.Places.DistanceTo(s, start)).FirstOrDefault()
            ?? planet.Pois.OrderBy(s => _game.Places.DistanceTo(s, start)).First();
        GD.Print($"NAT ruin {site.Family.Id} {site.Instance.State} {site.Instance.Age}, {Grown(site)} trees, biome {planet.Palette.BiomeAt(site.Origin.X, site.Origin.Z)}, {planet.Pois.Count(s => Grown(s) > 0)} of {planet.Pois.Count} places grew trees");
        Vector3 gate = Gate(site);
        Int3 size = site.Instance.Blocks.Size, f = site.Instance.Layout.EntranceFacing.Vec();
        Vector3 middle = planet.NearestCopy(new Vector3(site.Origin.X + site.Instance.Blocks.Min.X + size.X * 0.5f, site.Origin.Y + 2f, site.Origin.Z + site.Instance.Blocks.Min.Z + size.Z * 0.5f), gate);
        float back = Mathf.Max(size.X, size.Z) * 0.75f + 18f;
        Vector3 stand = Feet(middle + new Vector3(f.X, 0f, f.Z) * back);
        foreach (int x in GoTo(site, stand)) yield return x;
        foreach (int x in _drive.Wait(240)) yield return x;
        Vector3 eye = _game.Survivor.WorldFeet + Vector3.Up * 7f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(middle + Vector3.Up * 2f - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(20)) yield return x;
        NatShot("ruin");
        // The first tree close to: does it stand on what's under it, clear of the walls?
        var first = planet.Palette.Planted?.Values.FirstOrDefault(t => OnSite(site, t));
        if (first is { Scale: > 0f } tree)
        {
            Vector3 root = planet.NearestCopy(new Vector3((float)tree.X, tree.Foot, (float)tree.Z), gate);
            Vector3 near = root + new Vector3(f.X, 0f, f.Z) * 11f + new Vector3(-f.Z, 0f, f.X) * 6f + Vector3.Up * 4f;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(root + Vector3.Up * 3.5f - near, Vector3.Up), near);
            foreach (int x in _drive.Wait(20)) yield return x;
            GD.Print($"NAT tree {tree.Kind} scale {tree.Scale:F2}");
            NatShot("tree");
        }
        _game.FixedCamera = null;

        // Ore tells, one of each kind of tell, nearest the start that lie in the open.
        var tells = planet.Map.Deposits.InRect(0, 0, planet.Width, planet.Height)
            .Where(d => InfraLayer.HasTell(planet, d) && BiomeLook.FloraOf(planet.Palette.BiomeAt(Mathx.WrapX(d.X, planet.Width), Mathx.Wrap(d.Y, planet.Height))).Where(r => Planets.Surface.FloraMeshes.IsLarge(r.Kind)).Sum(r => r.Density) < 1f)
            .OrderBy(d => planet.NearestCopy(new Vector3((float)d.X, 0f, (float)d.Y), start).DistanceTo((start with { Y = 0f }))).ToList();
        GD.Print($"NAT {tells.Count} tells in the open, {planet.Map.Deposits.InRect(0, 0, planet.Width, planet.Height).Count(d => InfraLayer.HasTell(planet, d))} of {planet.Map.Deposits.Count} deposits show a tell");
        Vector3 field = start;
        foreach (var kind in new[] { GalHack.Deco.DecoTellKind.Outcrop, GalHack.Deco.DecoTellKind.Scree, GalHack.Deco.DecoTellKind.Fracture })
        {
            Deposit? pick = null;
            foreach (Deposit t in tells)
                if (InfraLayer.TellOf(t) == kind && (pick == null || kind != GalHack.Deco.DecoTellKind.Outcrop || t.Kind is DepositKind.Copper or DepositKind.Cobalt or DepositKind.Crystal or DepositKind.Burnstone)) { pick = t; if (kind != GalHack.Deco.DecoTellKind.Outcrop || t.Kind != DepositKind.Iron) break; }
            if (pick is not Deposit d) continue;
            Vector3 at = planet.NearestCopy(new Vector3((float)d.X, 0f, (float)d.Y), start);
            at = Feet(at);
            field = at;
            _game.Survivor.PlaceOutside(Feet(at + new Vector3(5f, 0f, 4f)).ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
            foreach (int x in _drive.Wait(300)) yield return x;
            foreach (int x in _drive.LookAt(at, 20)) yield return x;
            GD.Print($"NAT tell {kind} {d.Kind} depth {d.Depth}");
            NatShot($"tell-{kind.ToString().ToLowerInvariant()}");
        }

        // The field round the last of them, from a little above: how many, and how they read at a distance.
        Vector3 high = _game.Survivor.WorldFeet + Vector3.Up * 14f;
        Vector3 way = Vector3.Forward;
        int least = int.MaxValue;
        var plants = _game.Zone.Streamer.FloraNear(_game.Survivor.WorldFeet, 60f).Where(p => Planets.Surface.FloraMeshes.IsLarge(p.Kind)).ToList();
        for (int a = 0; a < 8; a++)
        {
            var dir = new Vector3(Mathf.Cos(a * Mathf.Pi / 4f), 0f, Mathf.Sin(a * Mathf.Pi / 4f));
            int n = plants.Count(p => (p.Root - high).Normalized().Dot(dir) > 0.75f);
            if (n < least) { least = n; way = dir; }
        }
        Vector3 ahead = Feet(high + way * 60f);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(ahead - high, Vector3.Up), high);
        foreach (int x in _drive.Wait(20)) yield return x;
        NatShot("field");
        _game.FixedCamera = null;

        // The nearest boulder.
        var near2 = _game.Zone.Streamer.FloraNear(_game.Survivor.WorldFeet, 200f).Where(p => p.Kind is Flora.Rock or Flora.LavaRock or Flora.Crystal or Flora.IceShard).OrderBy(p => p.Root.DistanceTo(_game.Survivor.WorldFeet)).Take(1).ToList();
        foreach (var rock in near2)
        {
            _game.Survivor.PlaceOutside(Feet(rock.Root + new Vector3(3.5f, 0f, 3f)).ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
            foreach (int x in _drive.Wait(120)) yield return x;
            foreach (int x in _drive.LookAt(rock.Root + Vector3.Up * 0.4f, 20)) yield return x;
            GD.Print($"NAT rock {rock.Kind}");
            NatShot("rock");
        }

        // Chips: cutting the ground, then an ore body.
        Vector3 here = _game.Survivor.WorldFeet;
        foreach (int x in Chips(here + new Vector3(2.2f, 0f, 0f), "chips-ground")) yield return x;
        Vector3? ore = null;
        foreach (DepositKind k in DepositFinder.Kinds)
            if (DepositFinder.Nearest(_game.Zone, k, here, 600f) is Vector3 found && (ore == null || found.DistanceTo(here) < ore.Value.DistanceTo(here))) ore = found;
        if (ore is Vector3 o)
        {
            Vector3 flat = o - here;
            flat.Y = 0f;
            _game.Survivor.PlaceOutside(Feet(o - flat.Normalized() * 2.5f).ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
            foreach (int x in _drive.Wait(90)) yield return x;
            int before = _game.Tool.OreCells;
            foreach (int x in _drive.HoldPrimary(o + Vector3.Up * 0.5f, 240, () => _game.Tool.OreCells > before)) yield return x;
            foreach (int x in Chips(o, "chips-ore")) yield return x;
        }
        _game.Controls.Primary = false;
    }

    /// <summary>Holds the tool on a point and snaps the moment a burst of chips is in the air.</summary>
    IEnumerable<int> Chips(Vector3 at, string name)
    {
        at = at with { Y = _game.Zone.GroundAt(at.X, at.Z).Y - 0.3f };
        var debris = _game.Tool.Debris;
        int before = debris.Bursts;
        foreach (int x in _drive.HoldPrimary(at, 300, () => debris.Bursts > before)) yield return x;
        _game.Controls.Primary = true;
        foreach (int x in _drive.Wait(7)) yield return x;
        NatShot(name);
        _game.Controls.Primary = false;
    }
}
