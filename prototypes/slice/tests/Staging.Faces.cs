using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using Slice.Places;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Close-ups of walls meeting the ground, to look at with your own eyes: --view=faces walks up to real
/// places and ruins of this world, one after another, and takes a shot of a corner from outside at a
/// person's height, then digs at the foot of a wall with the tool and looks down what it opened.
/// --out=DIR is the folder (one PNG a stop); --stops=N how many of each; --skip=N passes over the nearest.
/// </summary>
public sealed partial class Staging
{
    /// <summary>A shot taken part way through a view; set by the screenshot scene.</summary>
    public Action<string> TakeShot;
    public bool Snapping;

    IEnumerable<int> Shot(string file)
    {
        Snapping = true;
        TakeShot(file);
        while (Snapping) yield return 0;
    }

    /// <summary>Stands off a point with the ground at their feet, and looks at another.</summary>
    IEnumerable<int> StandAndLook(Vector3 stand, Vector3 look, int settle = 30)
    {
        stand.Y = _game.Zone.GroundAt(stand.X, stand.Z).Y + 0.2f;
        _game.Survivor.PlaceOutside(stand.ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(settle)) yield return x;
        foreach (int x in _drive.LookAt(look, 12)) yield return x;
    }

    IEnumerable<int> FacesView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.4);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        string dir = _args.GetValueOrDefault("out", "faces");
        System.IO.Directory.CreateDirectory(dir);
        int stops = (int)Num("stops", 5), skip = (int)Num("skip", 0);
        var planet = _game.Zone.Planet;
        Vector3 from = _game.Survivor.WorldFeet;
        var seen = new HashSet<string>();
        // The deepest first: basements are what the ground hides.
        List<PoiSite> sites = planet.Pois.Where(s => s.Instance != null).OrderBy(s => s.Footprint.Min.Y - s.Origin.Y).Where(s => seen.Add(s.Family.Id)).Skip(skip).Take(stops).ToList();
        int n = 0;
        foreach (PoiSite site in sites)
        {
            n++;
            GD.Print($"faces: stop {n}, {site.Name}, origin {site.Origin}, footprint {site.Footprint.Min} to {site.Footprint.Max}, siting {site.Siting}, half-buried {site.HalfBuried}");
            Vector3 lo = planet.NearestCopy(new Vector3(site.Footprint.Min.X, site.Origin.Y, site.Footprint.Min.Z), from);
            Vector3 hi = lo + new Vector3(site.Footprint.Max.X - site.Footprint.Min.X, 0f, site.Footprint.Max.Z - site.Footprint.Min.Z);
            Vector3 corner = Corner(site, lo, new Vector3(1f, 0f, 1f));
            foreach (int x in GoTo(site, lo + new Vector3(-4f, 0f, -4f))) yield return x;
            foreach (int x in StandAndLook(corner + new Vector3(-4.5f, 0f, -4.5f), corner + Vector3.Up * 0.9f)) yield return x;
            foreach (int x in Shot($"{dir}/poi-{n}-corner-a.png")) yield return x;
            Vector3 other = Corner(site, hi, new Vector3(-1f, 0f, -1f));
            foreach (int x in StandAndLook(other + new Vector3(4.5f, 0f, 4.5f), other + Vector3.Up * 0.9f)) yield return x;
            foreach (int x in Shot($"{dir}/poi-{n}-corner-b.png")) yield return x;
            // Dig at the foot of the wall with the tool, and look.
            Vector3 foot = other + new Vector3(-1.2f, 0f, 1.1f);
            float top = _game.Zone.GroundAt(foot.X, foot.Z).Y;
            foreach (int x in StandAndLook(foot + new Vector3(0f, 0f, 3.2f), foot)) yield return x;
            for (int k = 0; k < 6; k++)
                for (int side = -1; side <= 1; side++)
                    foreach (int x in _drive.HoldPrimary(new Vector3(foot.X + side * 1.1f, top - k * 0.7f, foot.Z), 34)) yield return x;
            foreach (int x in _drive.Wait(20)) yield return x;
            foreach (int x in StandAndLook(foot + new Vector3(0.4f, 0f, 3.4f), new Vector3(foot.X, top - 1.3f, foot.Z - 0.7f))) yield return x;
            foreach (int x in Shot($"{dir}/poi-{n}-dug.png")) yield return x;
            Place p = _game.Places.PlaceOf(site);
            if (p != null && p.Memory.Rooms.Count > 0)
            {
                var (room, eye, look) = RoomView(p);
                Int3Log(p, eye);
                _game.Survivor.PlaceOutside((eye + Vector3.Down * 1.55f).ToNumerics());
                foreach (int x in _drive.Wait(30)) yield return x;
                _game.Torch.Visible = true;
                foreach (int x in _drive.LookAt(look, 30)) yield return x;
                foreach (int x in _drive.Wait(30)) yield return x;
                foreach (int x in Shot($"{dir}/poi-{n}-room.png")) yield return x;
                _game.Torch.Visible = false;
            }
        }
        var ruins = planet.Infra.Ruins.Where(r => r.Blast < 0.05f).OrderBy(r => planet.NearestCopy(new Vector3((float)r.Center.X, from.Y, (float)r.Center.Z), from).DistanceTo(from)).Take(stops).ToList();
        n = 0;
        foreach (PlanetSurface.Infra.FlavourRuin ruin in ruins)
        {
            n++;
            GD.Print($"faces: ruin {n}, {ruin.Layout.Name} ({ruin.State}), origin ({ruin.OriginX}, {ruin.OriginY}, {ruin.OriginZ})");
            var t = ruin.Stamp;
            Vector3 lo = planet.NearestCopy(new Vector3(t.MinX, ruin.OriginY, t.MinZ), from);
            Vector3 hi = lo + new Vector3(t.MaxX - t.MinX, 0f, t.MaxZ - t.MinZ);
            _game.Survivor.PlaceOutside((lo + new Vector3(-4f, 2f, -4f)).ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
            foreach (int x in _drive.Wait(300)) yield return x;
            foreach (int x in StandAndLook(lo + new Vector3(-3.5f, 0f, -3.5f), lo + Vector3.Up * 0.9f)) yield return x;
            foreach (int x in Shot($"{dir}/ruin-{n}-corner-a.png")) yield return x;
            foreach (int x in StandAndLook(hi + new Vector3(3.5f, 0f, 3.5f), hi + Vector3.Up * 0.9f)) yield return x;
            foreach (int x in Shot($"{dir}/ruin-{n}-corner-b.png")) yield return x;
        }
    }

    /// <summary>What the eye of a room shot is in.</summary>
    void Int3Log(Place p, Vector3 eye)
    {
        var c = p.CellAt(eye);
        GD.Print($"faces: room eye cell {c}, {p.Inst.Blocks.Get(c).Type}, occupied {p.Inst.Layout.Occupied[c]}");
    }

    /// <summary>The outside corner of a place's blocks at ground level nearest a corner of its footprint, in the world.</summary>
    Vector3 Corner(PoiSite site, Vector3 at, Vector3 inward)
    {
        PoiGenerator.Gen.BlockGrid g = site.Instance.Blocks;
        Vector3 origin = at - new Vector3(site.Footprint.Min.X - site.Origin.X, 0f, site.Footprint.Min.Z - site.Origin.Z);
        if (inward.X < 0) origin = at - new Vector3(site.Footprint.Max.X - site.Origin.X, 0f, site.Footprint.Max.Z - site.Origin.Z);
        float best = float.MaxValue;
        Vector3 found = at;
        foreach (var (c, b) in g.Blocks())
        {
            if (c.Y != 1 || !PoiGenerator.Gen.BlockInfo.IsFullCube(b.Type)) continue;
            Vector3 p = origin + new Vector3(c.X + 0.5f, 0f, c.Z + 0.5f);
            float d = p.DistanceSquaredTo(at);
            if (d < best) { best = d; found = p; }
        }
        return found;
    }
}
