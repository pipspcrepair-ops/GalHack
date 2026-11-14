using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Tests;

public partial class PlaceTest
{
    /// <summary>
    /// -- --typed=airport (or airstrip, spaceport...): a body walks a typed place's world route on a fresh world with the game's own inputs (no pathfinding
    /// carries it; the place's own routes are walked a cell at a time): in at the landside gate, through the terminal to
    /// a stand on the apron, across to a hangar and onto its floor, to the tower and up it to the cab. Never below the
    /// field's level, never stuck, never rescued.
    /// </summary>
    IEnumerable<int> TypedWalk(string type)
    {
        (PoiSiting.ForceFamily, PoiSiting.ForceType) = (WorldChoice.Library.Families.First(f => f.Types.Contains(type)).Id, type);
        var game = new Game.Game { Interactive = false, World = WorldChoice.Fresh("temperate"), SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        (PoiSiting.ForceFamily, PoiSiting.ForceType) = (null, null);
        game.Zone.Weather.Bridge.Calm = true;
        GD.Print($"-- the {type} walk, {game.World} --");
        GD.Print("  (TEST: creatures held still on the walk)");
        game.Zone.Wildlife.Frozen = true;
        TypedPlace field = game.Zone.Planet.Pois.Select(p => p.Whole).FirstOrDefault(t => t != null);
        if (field == null) { Fail($"a {type} on {game.World}"); game.QueueFree(); yield break; }
        GD.Print($"  ({field.Why})");
        var pieces = PlaceTypes.Pieces(field.Sketch, field.Turn);
        string Kind(PoiSite s) => pieces[field.Parts.IndexOf(s)].Plan.Parts[0].Arg("what");
        int rescues = 0;
        game.Zone.Physics.Rescued += r => { if (r.Body == game.Survivor.Body) rescues++; };
        float lowest = 0f;
        string lowAt = "";
        IEnumerable<int> Watched(IEnumerable<int> steps)
        {
            foreach (int x in steps)
            {
                float under = field.PadY - game.Survivor.WorldFeet.Y;
                if (under > lowest) (lowest, lowAt) = (under, $"{game.Survivor.WorldFeet}");
                yield return x;
            }
        }
        Vector3 World(float x, float y, float z) => game.Zone.Planet.NearestCopy(new Vector3(x, y, z), game.Survivor.WorldFeet);
        var walk = new PlaceWalk(game, drive);
        Vector3? leave = null;
        foreach (int x in drive.ClimbOut(game.Bunker)) yield return x;
        var gate = field.Anchor.Gates[0];
        game.Survivor.PlaceOutside((World(gate.Cell.X + 0.5f, field.PadY + 0.3f, gate.Cell.Z + 0.5f)).ToNumerics());
        foreach (int x in drive.UntilSettled(30)) yield return x;

        // Each a place of its own: up to its way in over the made ground (the terminal from its landside, the rest from the
        // apron, 30 m out first so the way round never cuts across a neighbour), then its own routes to the room.
        // (A spaceport's main building is its hub's tower or its authority building: up to its cab or its operations room.)
        string main = Kind(field.Anchor);
        foreach (var (kind, goals) in new[] { ("terminal", new[] { "gate stand", "stand", "control cab", "flight operations" }), ("hangar", new[] { "hangar floor" }), ("tower", new[] { "control cab" }) })
        {
            PoiSite site = kind == "terminal" ? field.Anchor : field.Parts.FirstOrDefault(p => Kind(p) == kind);
            if (site == null && kind == "tower" && main is "hub" or "authority") continue;
            if (site == null) { Fail($"the {field.Sketch.Type.Id} has a {kind}"); continue; }
            float cx = (site.Footprint.Min.X + site.Footprint.Max.X) * 0.5f, cz = (site.Footprint.Min.Z + site.Footprint.Max.Z) * 0.5f;
            Vector3 front = new Vector3(site.Entrance.X + 0.5f - cx, 0f, site.Entrance.Z + 0.5f - cz);
            float reach = front.Length();
            front /= reach;
            // (An airfield's terminal's front is its airside: its landside way in is the other way. A port's hub or
            // authority building is come to from its front, as everything else.)
            bool landside = kind == "terminal" && main == "terminal";
            Vector3 way = landside ? World(cx, field.PadY, cz) - front * reach : World(site.Entrance.X + 0.5f, field.PadY, site.Entrance.Z + 0.5f);
            // (Out of the last one by its own way in first, as a player leaves a hall. Across a spaceport's field, whose
            // recessed pads are pits a body walking straight would fall into, set down 30 m out from the next.)
            if (leave is { } back) foreach (int x in Watched(drive.WalkTo(back, 2f, 120f, dig: false, hop: false))) yield return x;
            if (!landside && field.Sketch.Type.Plan != null)
            {
                game.Survivor.PlaceOutside((way + front * 30f + Vector3.Up * 0.3f).ToNumerics());
                foreach (int x in drive.UntilSettled(30)) yield return x;
            }
            if (!landside) foreach (int x in Watched(drive.WalkTo(way + front * 30f, 2f, 120f, dig: false, hop: false))) yield return x;
            leave = landside ? null : way + front * 30f;
            foreach (int x in Watched(drive.WalkTo(way, 1.2f, 120f, sprint: false, dig: false, hop: false))) yield return x;
            for (int i = 0; i < 1800 && game.Places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;
            Place place = game.Places.PlaceOf(site);
            if (place == null) { Fail($"the {kind} stood up as a place"); continue; }
            var nav = place.Nav();
            var room = goals.Select(g => place.Inst.Layout.Modules.FirstOrDefault(m => m.Purpose == g)).FirstOrDefault(m => m != null);
            var reachable = nav.Reachable(walk.Here(place, nav));
            List<PInt3> cells = room?.Cells.Where(reachable.Contains).ToList() ?? new();
            if (cells.Count == 0) { Fail($"the {kind}'s {goals[0]} is on foot from where the body stands ({walk.Here(place, nav)})"); continue; }
            PInt3 goal = cells[cells.Count / 2];
            bool ok = false;
            int stuck = walk.Stuck;
            foreach (int x in Watched(walk.To(place, goal, r => ok = r))) yield return x;
            Check(ok && room.Cells.Contains(walk.Here(place, nav)) && walk.Stuck == stuck,
                $"walked to the {kind}'s {room.Purpose} on foot{(walk.Notes.Count > 0 ? $" ({string.Join("; ", walk.Notes)})" : "")}");
            walk.Notes.Clear();
        }
        Check(rescues == 0 && lowest < 0.6f, $"never below the field's level ({lowest:F2} m at worst{(lowest > 0f ? $", at {lowAt}" : "")}), never rescued ({rescues}); {walk.DoorsOpened} doors opened, {walk.LadderCells} ladder cells climbed");
        game.QueueFree();
        for (int i = 0; i < 10; i++) yield return 0;
    }
}
