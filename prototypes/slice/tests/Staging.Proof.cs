using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Places;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Before-and-after frames for fixes, framed the same way on any build: a place's module by its purpose
/// (the camera well clear of every surface, the room lit by day and the torch), a door's edge, a shore.
/// </summary>
public partial class Staging
{
    IEnumerable<int> ProofViews(string view) => view switch
    {
        "proof-room" => ProofRoom(),
        "proof-door" => ProofDoor(),
        "proof-shore" => ProofShore(),
        _ => OrbitViews(view),
    };

    /// <summary>A point of the place's grid where a person stands, nearest a wanted spot: its floor in the world.</summary>
    static Vector3? StandNear(Place p, Func<Int3, bool> allowed, Vector3 local)
    {
        NavGrid nav = p.Nav();
        Int3? best = null;
        float bestD = float.MaxValue;
        for (int z = -6; z <= 6; z++)
            for (int x = -6; x <= 6; x++)
                for (int y = -2; y <= 2; y++)
                {
                    var c = new Int3((int)MathF.Floor(local.X) + x, (int)MathF.Floor(local.Y) + y, (int)MathF.Floor(local.Z) + z);
                    if (!allowed(c) || !nav.Standable(c)) continue;
                    float d = new Vector3(c.X + 0.5f - local.X, c.Y - local.Y, c.Z + 0.5f - local.Z).Length();
                    if (d < bestD) { bestD = d; best = c; }
                }
        return best is Int3 b ? p.ToWorld(new Vector3(b.X + 0.5f, b.Y, b.Z + 0.5f)) : null;
    }

    /// <summary>
    /// A module of a place by its purpose (--family, --purpose: the largest of that purpose), seen from 1.3 m
    /// in from its corner at eye height, at noon with the torch on.
    /// </summary>
    IEnumerable<int> ProofRoom()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.45);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        PoiSite site = Named();
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        Place p = _game.Places.PlaceOf(site);
        string want = _args.GetValueOrDefault("purpose", "drift");
        PlacedModule m = p.Inst.Layout.Modules.Where(q => q.Purpose == want).OrderByDescending(q => q.Cells.Count()).FirstOrDefault()
            ?? p.Inst.Layout.Modules.Where(q => q.Kind is ModuleKind.Room or ModuleKind.Hall).OrderByDescending(q => q.Cells.Count()).First();
        var own = new HashSet<Int3>(m.Cells);
        int floorY = m.Cells.Where(c => p.Nav().Standable(c)).Select(c => c.Y).DefaultIfEmpty(m.Min.Y).Min();
        Int3 size = m.Max - m.Min + new Int3(1, 1, 1);
        Vector3 from = new(m.Min.X + 1.8f, floorY, m.Min.Z + 1.8f), to = new(m.Min.X + size.X * 0.6f, floorY, m.Min.Z + size.Z * 0.6f);
        Vector3 feet = StandNear(p, own.Contains, from) ?? p.ToWorld(from);
        GD.Print($"(proof room: {site.Name} [{site.Family.Id}], {m.Purpose} module {m.Index}, {size}, feet {feet})");
        _game.Survivor.PlaceOutside(feet.ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        _game.Torch.Visible = true;
        foreach (int x in _drive.LookAt(p.ToWorld(to) + Vector3.Up * 0.7f, 30)) yield return x;
        foreach (int x in _drive.Wait(90)) yield return x;
    }

    /// <summary>The nearest shut door of a place (--family), seen side on from 1.5 m off its edge, by day.</summary>
    IEnumerable<int> ProofDoor()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.45);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        PoiSite site = Named();
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        Place p = _game.Places.PlaceOf(site);
        Place.Door d = p.Doors.Where(q => q.Jammed).Concat(p.Doors).First();
        // Half open, so the leaves' inside edges show.
        d.Target = 0.5f;
        d.Open = 0.45f;
        Vector3 across = new Vector3(d.Along.Z, 0f, -d.Along.X);
        Vector3 centre = p.ToWorld(d.Centre);
        Vector3? feet = StandNear(p, _ => true, d.Centre + across * 1.7f + d.Along * 0.6f - Vector3.Up * (d.Centre.Y - MathF.Floor(d.Centre.Y)));
        _game.Survivor.PlaceOutside((feet ?? centre + across * 1.7f).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        _game.Torch.Visible = true;
        foreach (int x in _drive.LookAt(centre, 30)) yield return x;
        foreach (int x in _drive.Wait(60)) yield return x;
    }

    /// <summary>The sea's edge nearest the start: standing on the shore 6 m from it, looking along it, by day.</summary>
    IEnumerable<int> ProofShore()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        var planet = _game.Zone.Planet;
        Vector3 start = _game.Survivor.WorldFeet;
        // Walk out in rings from the start until the map's sea margin is crossed on gentle ground.
        Vector3? shore = null, seaward = null;
        for (float r = 20f; r < 6000f && shore == null; r += 20f)
            for (int a = 0; a < 64 && shore == null; a++)
            {
                float ang = a * MathF.Tau / 64f;
                Vector3 at = start + new Vector3(MathF.Cos(ang), 0, MathF.Sin(ang)) * r, on = at + new Vector3(MathF.Cos(ang), 0, MathF.Sin(ang)) * 4f;
                float e0 = planet.Map.Sample(planet.Map.Elevation, Planets.Gen.Mathx.WrapX(at.X, planet.Width), Planets.Gen.Mathx.Wrap(at.Z, planet.Height));
                float e1 = planet.Map.Sample(planet.Map.Elevation, Planets.Gen.Mathx.WrapX(on.X, planet.Width), Planets.Gen.Mathx.Wrap(on.Z, planet.Height));
                if (e0 >= -GalHack.Terrain.PlanetTerrainSource.SeaMaskDepth && e1 < -GalHack.Terrain.PlanetTerrainSource.SeaMaskDepth && planet.GroundHeight(at.X, at.Z) < 2f)
                {
                    shore = at;
                    seaward = (on - at).Normalized();
                }
            }
        if (shore is not Vector3 s) { GD.Print("(proof shore: no sea near)"); yield break; }
        Vector3 side = new(seaward.Value.Z, 0f, -seaward.Value.X);
        Vector3 feet = s - seaward.Value * 6f;
        feet.Y = _game.Zone.GroundAt(feet.X, feet.Z).Y + 0.1f;
        GD.Print($"(proof shore: {s}, looking along it)");
        _game.Survivor.PlaceOutside(feet.ToNumerics());
        foreach (int x in _drive.Wait(240)) yield return x;
        foreach (int x in _drive.LookAt(s + side * 14f + seaward.Value * 3f, 30)) yield return x;
        foreach (int x in _drive.Wait(90)) yield return x;
    }
}
