using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using PlanetSurface.Poi;
using Slice.Construction;
using Slice.Game;
using Slice.Places;
using Slice.Ui;
using Slice.Vessels;
using Structure = GalHack.Structures.Structure;
using N = System.Numerics;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Proof frames of the one door, by day, a 1.8 m figure standing at it: "doorway" is a place's way in
/// (--family=a,b,c, the nearest of the first the world has), "doorblock" the player's one-block door set
/// into the Outpost's wall by the building rules, shut (the view's --out) and open (the same name, -open).
/// </summary>
public partial class Staging
{
    IEnumerable<int> DoorViews(string view) => view switch
    {
        "doorway" => Doorway(),
        "doorblock" => DoorBlock(),
        _ => ProofViews(view),
    };

    /// <summary>A plain figure 1.8 m tall, its feet on a point: the person every door is measured against.</summary>
    Node3D Figure(Vector3 feet)
    {
        var body = new MeshInstance3D
        {
            Name = "Figure 1.8 m", Mesh = new CapsuleMesh { Radius = 0.25f, Height = 1.8f }, Position = feet + Vector3.Up * 0.9f,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.35f, 0.2f), Roughness = 0.8f },
        };
        _game.Zone.AddChild(body);
        return body;
    }

    /// <summary>Frames a door from out in front and a little to one side, eye high, the figure beside it.</summary>
    IEnumerable<int> FrameDoor(Vector3 foot, Vector3 outward, Vector3 along, float width)
    {
        Figure(foot + outward * 1.5f + along * (width * 0.3f));
        Vector3 eye = foot + outward * 7.5f - along * 2.5f + Vector3.Up * 1.7f;
        // The sleeper stands behind the camera, out of the frame.
        Vector3 behind = foot + outward * 10f;
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(behind.X, behind.Z) + Vector3.Up * 0.2f).ToNumerics());
        foreach (int x in _drive.UntilSettled(20)) yield return x;
        _game.FixedCamera = new Transform3D(Basis.LookingAt((foot + Vector3.Up * 1.6f - eye).Normalized(), Vector3.Up), eye);
        if (_args.GetValueOrDefault("hud") == "0") _game.Visor.Visible = false;
        // Daylight where the door is: the clock tells the map middle's solar time, and the hour runs with the longitude.
        double lon = ((foot.X / _game.Zone.Planet.Width) % 1.0 + 1.0) % 1.0;
        _game.Zone.Sky.TimeOfDay = ((Num("time", 0.45) - Num("lonsign", 1) * (lon - 0.5)) % 1.0 + 1.0) % 1.0;
        foreach (int x in _drive.Wait(90)) yield return x;
    }

    /// <summary>A place's way in by day, a figure at its door; --ruin=shop (or wayside, shed...) a small roadside ruin's instead.</summary>
    IEnumerable<int> Doorway()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.45);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        if (_args.TryGetValue("ruin", out string what))
        {
            var planet = _game.Zone.Planet;
            Vector3 from = _game.Survivor.WorldFeet;
            float Away(PlanetSurface.Infra.FlavourRuin r) => planet.NearestCopy(new Vector3((float)r.Center.X, from.Y, (float)r.Center.Z), from).DistanceTo(from);
            static List<PoiGenerator.Gen.Int3> WayIn(PoiGenerator.Gen.Layout l) => l.ExitCells.Count > 0 ? l.ExitCells
                : l.Doorways.Where(d => d.ModuleA == l.EntranceModule || d.ModuleB == l.EntranceModule).Select(d => d.Cells).FirstOrDefault() ?? new List<PoiGenerator.Gen.Int3>();
            // --jammed=1: one whose way in has a door stuck part open (spawned here as its tile spawns it).
            bool jammed = _args.GetValueOrDefault("jammed") == "1";
            bool Stuck(PlanetSurface.Infra.FlavourRuin r)
            {
                if (!jammed) return true;
                var inst = r.Spawn(planet.Profile, planet.Source);
                return WayIn(r.Layout).Any(c => inst.Blocks.Get(c) is { Type: PoiGenerator.Gen.BlockType.Door } b && b.Variant == PoiGenerator.Gen.DoorState.Jammed);
            }
            var ruin = what.Split(',').Select(t => planet.Infra.Ruins.Where(r => r.Blast < 0.05f && r.Family.Tags.Contains(t) && WayIn(r.Layout).Count > 0).OrderBy(Away).Take(jammed ? 60 : 1).FirstOrDefault(Stuck)).FirstOrDefault(r => r != null);
            if (ruin == null) GD.Print($"(doorway: ruins here: {string.Join(", ", planet.Infra.Ruins.GroupBy(r => r.Family.Id).Select(g => $"{g.Key} {g.Count()}"))})");
            if (ruin == null) { GD.Print($"(doorway: no {what} ruin on this world)"); yield break; }
            var exit = WayIn(ruin.Layout);
            PoiGenerator.Gen.Int3 l0 = exit.Aggregate(PoiGenerator.Gen.Int3.Min), h0 = exit.Aggregate(PoiGenerator.Gen.Int3.Max), f0 = PoiGenerator.Gen.Dirs.Vec(ruin.Layout.EntranceFacing);
            Vector3 at = planet.NearestCopy(new Vector3(ruin.OriginX + (l0.X + h0.X + 1) * 0.5f, ruin.OriginY + l0.Y, ruin.OriginZ + (l0.Z + h0.Z + 1) * 0.5f), from);
            Vector3 o0 = new(f0.X, 0f, f0.Z);
            Vector3 near = at + o0 * 12f;
            _game.Survivor.PlaceOutside((_game.Zone.GroundAt(near.X, near.Z) + Vector3.Up * 0.2f).ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
            foreach (int x in _drive.Wait(600)) yield return x;
            GD.Print($"(doorway: {ruin.Layout.Name} [{ruin.Family.Id}] {ruin.State}, {h0.Y - l0.Y + 1} m tall, at {at})");
            foreach (int x in FrameDoor(at, o0, new Vector3(MathF.Abs(f0.Z), 0f, MathF.Abs(f0.X)), Math.Max(h0.X - l0.X, h0.Z - l0.Z) + 1)) yield return x;
            yield break;
        }
        // The nearest standing place of the families named (a ruin's way in may have fallen), else the nearest at all.
        Vector3 here = _game.Survivor.WorldFeet;
        var named = _args.GetValueOrDefault("family", "").Split(',');
        PoiSite site = _game.Zone.Planet.Pois.Where(x => named.Contains(x.Family.Id) && x.Instance?.State is not PoiGenerator.Gen.PoiState.Ruined)
            .OrderBy(x => Array.IndexOf(named, x.Family.Id)).ThenBy(x => _game.Places.DistanceTo(x, here)).FirstOrDefault() ?? Proofed();
        if (site == null)
        {
            GD.Print($"(doorway: this world has {string.Join(", ", _game.Zone.Planet.Pois.GroupBy(x => x.Family.Id).Select(g => $"{g.Key} {g.Count()}"))})");
            yield break;
        }
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        Place p = _game.Places.PlaceOf(site);
        var layout = p.Inst.Layout;
        var cells = layout.ExitCells.Count > 0 ? layout.ExitCells : layout.Doorways.OrderBy(d => (new Vector3(d.Cells[0].X, 0, d.Cells[0].Z) - new Vector3(layout.EntranceCell.X, 0, layout.EntranceCell.Z)).Length()).First().Cells;
        PoiGenerator.Gen.Int3 lo = cells.Aggregate(PoiGenerator.Gen.Int3.Min), hi = cells.Aggregate(PoiGenerator.Gen.Int3.Max);
        PoiGenerator.Gen.Int3 o = PoiGenerator.Gen.Dirs.Vec(layout.EntranceFacing);
        Vector3 outward = new(o.X, 0f, o.Z), along = new(MathF.Abs(o.Z), 0f, MathF.Abs(o.X));
        Vector3 foot = p.ToWorld(new Vector3((lo.X + hi.X + 1) * 0.5f, lo.Y, (lo.Z + hi.Z + 1) * 0.5f));
        GD.Print($"(doorway: {site.Name} [{site.Family.Id}], {hi.Y - lo.Y + 1} m tall, {Math.Max(hi.X - lo.X, hi.Z - lo.Z) + 1} m wide, at {foot})");
        foreach (int x in FrameDoor(foot, outward, along, Math.Max(hi.X - lo.X, hi.Z - lo.Z) + 1)) yield return x;
    }

    /// <summary>
    /// The Outpost set down through the Blueprints tab, then a door placed into its west wall by the building
    /// rules: a wall block taken out, the door (one block, 2 m) put in. Shut, then open.
    /// </summary>
    IEnumerable<int> DoorBlock()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.45);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Mode = GameMode.Creative; // STAGING: free blocks
        BlueprintsSection tab = _game.Menu.Root.Blueprints;
        LibraryEntry e = tab.Library.Starters.FirstOrDefault(x => x.File == "outpost.gbp");
        if (e == null) { GD.Print("(no outpost)"); yield break; }
        Vessel v = null;
        for (int attempt = 0; attempt < 4 && v == null; attempt++)
        {
            Vector3 spot = Play.FlatSpot(_game.Start, 60f + 40f * attempt, 2.2f, 0.5f + attempt * 0.5f, span: 16f);
            Vector3 stand = spot + Vector3.Back * 14f;
            _game.Survivor.PlaceOutside((_game.Zone.GroundAt(stand.X, stand.Z) + Vector3.Up * 0.2f).ToNumerics());
            foreach (int x in _drive.UntilSettled(20)) yield return x;
            _game.Survivor.Yaw = 0f;
            _game.Survivor.Pitch = 0f;
            foreach (int x in Play.OpenMenu(MenuTab.Blueprints)) yield return x;
            foreach (int x in _drive.ClickWidget($"bp:entry:{tab.Library.All.ToList().IndexOf(e)}")) yield return x;
            foreach (int x in _drive.ClickWidget("bp:place")) yield return x;
            WholePlacement job = _game.Builder.Placing;
            for (int t = 0; t < 600 && job != null && !job.Done; t++) yield return 0;
            v = job?.Vessel;
            foreach (int x in Play.CloseMenu()) yield return x;
        }
        if (v == null) { GD.Print("(the outpost would not go down)"); yield break; }
        Structure s = v.Structure;
        // The west wall (x -3): take a block out of its foot and put the door in.
        var foot = new Int3(-3, 0, -3);
        Building.Remove(s, foot, null);
        Orientations.TryParse("+y-x", out int rot);
        PlaceError placed = Building.Place(s, foot, Building.Make(s, s.Catalogue.Type("door"), null, rot));
        v.ApplyEdits();
        GD.Print($"(door block: {placed}, {s.TypeOf(s.Get(foot)).Id}, over it {(s.Has(foot + Int3.UnitY) ? s.TypeOf(s.Get(foot + Int3.UnitY)).Id : "nothing")})");
        foreach (int x in _drive.Wait(30)) yield return x;
        float size = s.Grid.CellSize;
        Vector3 bottom = v.ToWorld(StructureBridge.CellCenter(s, foot) - N.Vector3.UnitY * (size * 0.5f));
        Vector3 outward = (v.ToWorld(StructureBridge.CellCenter(s, foot) - N.Vector3.UnitX) - v.ToWorld(StructureBridge.CellCenter(s, foot))).Normalized();
        Vector3 along = new(-outward.Z, 0f, outward.X);
        foreach (int x in FrameDoor(bottom, outward, along, 0.88f * size)) yield return x;
        string shut = _args.GetValueOrDefault("out", "doorblock.png");
        foreach (int x in Shot(shut)) yield return x;
        // Your own doors open as you come up and shut behind you: held open as if someone stood in it.
        Func<Vector3> feet = _game.Doors.Feet;
        Vector3 inDoor = v.ToWorld(StructureBridge.CellCenter(s, foot)) - Vector3.Up * 0.9f;
        _game.Doors.Feet = () => inDoor;
        _game.Doors.Use(v, foot);
        foreach (int x in _drive.Wait(120)) yield return x;
        GD.Print($"(door block open: {s.Get(foot).Open})");
        foreach (int x in Shot(shut.Replace(".png", "-open.png"))) yield return x;
        _game.Doors.Feet = feet;
        foreach (int x in _drive.Wait(120)) yield return x;
    }
}
