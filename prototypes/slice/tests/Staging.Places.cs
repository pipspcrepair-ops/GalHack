using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Slice.Surface;
using Slice.Ui;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The screenshot views of the planet's places. A view stages its moment by moving the castaway
/// there (screenshots only; the real-player test walks), lets the place stand up in its own time,
/// and then uses the game's own controls.
/// </summary>
public sealed partial class Staging
{
    /// <summary>Puts the castaway on a spot and waits until the place near it stands and the ground is in.</summary>
    IEnumerable<int> GoTo(PoiSite site, Vector3 feet)
    {
        _game.Survivor.PlaceOutside(feet.ToNumerics());
        for (int i = 0; i < 1800 && _game.Places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(60)) yield return x;
    }

    PoiSite Named(string fallbackTag = null)
    {
        Vector3 from = _game.Survivor.WorldFeet;
        if (_args.TryGetValue("family", out string family))
            return _game.Zone.Planet.Pois.Where(s => s.Family.Id == family).OrderBy(s => _game.Places.DistanceTo(s, from)).First();
        return _game.Bunker.Poi;
    }

    Vector3 Gate(PoiSite site) => _game.Zone.Planet.NearestCopy(new Vector3(site.Entrance.X + 0.5f, site.Entrance.Y, site.Entrance.Z + 0.5f), _game.Survivor.WorldFeet);

    /// <summary>Walking up to the place in view: its door ahead, the land round it.</summary>
    IEnumerable<int> PlaceApproach()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.36);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        PoiSite site = Named();
        Vector3 gate = Gate(site);
        Int3 f = site.Instance.Layout.EntranceFacing.Vec();
        Vector3 stand = gate + new Vector3(f.X, 0f, f.Z) * 26f;
        stand.Y = _game.Zone.GroundAt(stand.X, stand.Z).Y + 0.2f;
        foreach (int x in GoTo(site, stand)) yield return x;
        foreach (int x in _drive.LookAt(gate + Vector3.Up * 3f, 20)) yield return x;
    }

    /// <summary>A room's corner: pick the fullest dressed room above ground or near the way in, the camera in one corner at eye height.</summary>
    (PlaceRoom Room, Vector3 Eye, Vector3 Look) RoomView(Place p)
    {
        string want = _args.GetValueOrDefault("roomid", "mess");
        PlaceRoom room = p.Memory.Rooms.Values.OrderByDescending(r => (r.Room.RoomId == want ? 1000 : 0) + r.Plan.Placements.Count - MathF.Abs(r.Corner.Y) * 2f).First();
        var size = new Vector3(room.Room.Width, 0f, room.Room.Depth);
        Vector3 corner = p.ToWorld(new Vector3(room.Corner.X, room.Corner.Y, room.Corner.Z));
        return (room, corner + new Vector3(0.35f, 1.55f, 0.35f), corner + size * 0.75f + Vector3.Up * 0.6f);
    }

    /// <summary>A dressed room, its age and state on it.</summary>
    IEnumerable<int> PlaceInterior()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.4);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        PoiSite site = Named();
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        Place p = _game.Places.PlaceOf(site);
        var (room, eye, look) = RoomView(p);
        _game.Survivor.PlaceOutside((eye + Vector3.Down * 1.55f).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        _game.Torch.Visible = true;
        foreach (int x in _drive.LookAt(look, 30)) yield return x;
        foreach (int x in _drive.Wait(60)) yield return x;
    }

    /// <summary>A creature's tell: beside a hunter when it gives itself away (grit, a shadow, a scratch).</summary>
    IEnumerable<int> PlaceTell()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.4);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        PoiSite site = Named();
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        Place p = _game.Places.PlaceOf(site);
        DeepSite deep = _game.Zone.Wildlife.Sites.First(s => s.Sim == p.Memory.Fauna.Sim);
        var nav = p.Nav();
        var open = nav.Reachable(p.Inst.Layout.EntranceCell);
        var hunter = deep.Sim.Creatures.Where(c => !c.Dead && c.Species.Role == Wildlife.DeepRole.Ambusher && c.Species.Tells.Length > 0).OrderBy(c => c.Position.Y < -6f ? 1 : 0).First();
        var cell = new Int3(Mathf.FloorToInt(hunter.Position.X), Mathf.FloorToInt(hunter.Position.Y), Mathf.FloorToInt(hunter.Position.Z));
        Int3 stand = open.Where(c => new Vector2(c.X - cell.X, c.Z - cell.Z).Length() is > 3f and < 7f && Mathf.Abs(c.Y - cell.Y) <= 3).OrderBy(c => Mathf.Abs(new Vector2(c.X - cell.X, c.Z - cell.Z).Length() - 4.5f)).First();
        _game.Survivor.PlaceOutside(PlaceWalk.Feet(p, stand).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        _game.Torch.Visible = true;
        foreach (int x in _drive.LookAt(deep.World(hunter.Position) + Vector3.Up * 0.8f, 20)) yield return x;
        Wildlife.TellEvent? told = null;
        deep.Told += e => { if (e.Target == deep.Player && e.Kind is Wildlife.TellKind.Dust or Wildlife.TellKind.Shadow or Wildlife.TellKind.Rattle or Wildlife.TellKind.Thump) told = e; };
        // STAGING: only this hunter wakes; the rest of the place is taken out of the shot.
        foreach (var c in deep.Sim.Creatures.Where(c => c != hunter)) deep.Sim.Remove(c);
        _game.Zone.Wildlife.Frozen = false;
        for (int i = 0; i < 1800 && told == null; i++)
        {
            _drive.Face(deep.World(hunter.Position) + Vector3.Up * 0.8f);
            yield return 0;
        }
        if (told is Wildlife.TellEvent t)
            for (int i = 0; i < 16; i++)
            {
                _drive.Face(deep.World(t.Position));
                yield return 0;
            }
        _game.Zone.Wildlife.Frozen = true;
    }

    /// <summary>The loot view on a place's cache, opened with E.</summary>
    IEnumerable<int> PlaceCache()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.4);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        PoiSite site = Named();
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        Place p = _game.Places.PlaceOf(site);
        var nav = p.Nav();
        LootCache cache = p.Inst.Caches.Where(c => !c.Secret && c.Items.Count > 0 && nav.AccessCell(c.Cell) is Int3).OrderByDescending(c => c.Items.Count).First();
        _game.Survivor.PlaceOutside(PlaceWalk.Feet(p, nav.AccessCell(cache.Cell).Value).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _drive.LookAt(p.WorldOf(cache.Cell, 0.4f), 10)) yield return x;
        foreach (int x in _drive.Tap(Key.E)) yield return x;
        foreach (int x in _drive.Wait(20)) yield return x;
    }

    /// <summary>A settlement with its people about.</summary>
    IEnumerable<int> TownView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.38);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Vector3 from = _game.Survivor.WorldFeet;
        PoiSite site = _game.Zone.Planet.Pois.Where(s => s.Instance?.Townsfolk != null).OrderBy(s => _game.Places.DistanceTo(s, from)).First();
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        Place p = _game.Places.PlaceOf(site);
        foreach (int x in _drive.Wait(600)) yield return x;
        Vector3 middle = p.ToWorld(new Vector3(p.Memory.Town.Data.AreaCenter.X, 1.2f, p.Memory.Town.Data.AreaCenter.Z));
        foreach (int x in _drive.LookAt(middle, 20)) yield return x;
    }

    /// <summary>Salvaging a wreck: the tool at work on its hull, a piece coming away.</summary>
    IEnumerable<int> Salvage()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.36);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Vector3 from = _game.Survivor.WorldFeet;
        PoiSite site = _game.Zone.Planet.Pois.Where(s => s.Family.Tier == PoiTier.Wreck).OrderBy(s => _game.Places.DistanceTo(s, from)).First();
        GD.Print($"salvaging {site.Name}");
        Vector3 gate = Gate(site);
        foreach (int x in GoTo(site, gate + Vector3.Up * 0.2f)) yield return x;
        Place p = _game.Places.PlaceOf(site);
        // The nearest block of its hull at chest height, from where you stand.
        Int3 here = p.CellAt(_game.Survivor.WorldFeet);
        var (cell, block) = p.Inst.Blocks.Blocks().Where(b => b.Block.Type == BlockType.Cube && _game.Places.Yield(b.Block) != null && b.Cell.Y >= here.Y && b.Cell.Y <= here.Y + 2)
            .OrderBy(b => b.Cell.Manhattan(here)).First();
        Vector3 at = p.WorldOf(cell);
        Vector3 back = (_game.Survivor.WorldFeet - at) with { Y = 0f };
        Vector3 stand = at + (back.LengthSquared() > 0.1f ? back.Normalized() : Vector3.Back) * 2.6f;
        stand.Y = _game.Survivor.WorldFeet.Y;
        foreach (int x in _drive.WalkTo(stand, 0.6f, 10f, sprint: false, dig: false, hop: false)) yield return x;
        foreach (int x in Play.Tool(Slice.Player.ToolMode.Gather)) yield return x;
        foreach (int x in _drive.HoldPrimary(at, 200)) yield return x;
        _game.Controls.Primary = true;
        foreach (int x in _drive.LookAt(at, 6)) yield return x;
    }

    /// <summary>The chamber close up, from a shallow angle along the floor: the lockers on the wall, the pods with their numbers.</summary>
    IEnumerable<int> BunkerClose()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.3);
        Slice.Start.Bunker b = _game.Bunker;
        foreach (int x in _drive.WalkTo(b.RoomCentre + Vector3.Back * 0.2f, 0.5f, 10f)) yield return x;
        string what = _args.GetValueOrDefault("at", "lockers");
        Vector3 eye = what switch
        {
            "pods" => b.At(new Vector3(0.4f, 1.2f, -0.2f)),
            "floor" => b.At(new Vector3(2.6f, 0.42f, -1.8f)),
            _ => b.At(new Vector3(-2.2f, 1.5f, -1.9f)),
        };
        Vector3 look = what switch
        {
            "pods" => b.At(new Vector3(1.0f, 1.5f, 2f)),
            "floor" => b.At(new Vector3(-2f, 0.3f, 1.4f)),
            _ => b.At(new Vector3(3.1f, 1.2f, -0.2f)),
        };
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(30)) yield return x;
    }
}
