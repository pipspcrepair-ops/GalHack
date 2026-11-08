using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Places;

namespace Slice.Tests;

/// <summary>
/// A capital hulk on the world as it rolled, weather and all (run with --force=crashed_freighter so one lies
/// near the start): from the ground at a distance, from the air, then its bridge, its engine room and a
/// hold from inside, frame by frame into --dir, each name led by --tag.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> HulkView()
    {
        string dir = _args.GetValueOrDefault("dir", "."), tag = _args.GetValueOrDefault("tag", "hulk");
        System.IO.Directory.CreateDirectory(dir);
        _game.Zone.Wildlife.Frozen = true;
        _game.Visor.Visible = false;
        PoiSite site = Named();
        GD.Print($"hulk: {site.Family.Id} '{site.Instance.Layout.Variant?.Name}' {site.Instance.State}");
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        Place p = _game.Places.PlaceOf(site);
        PoiInstance inst = p.Inst;
        var hull = inst.Blocks.Blocks().Where(b => b.Block.Role is Role.Wall or Role.Roof).Select(b => b.Cell).ToList();
        Vector3 lo = new(hull.Min(c => c.X), hull.Min(c => c.Y), hull.Min(c => c.Z)), hi = new(hull.Max(c => c.X), hull.Max(c => c.Y), hull.Max(c => c.Z));
        Vector3 mid = p.ToWorld((lo + hi) * 0.5f);
        bool alongZ = hi.Z - lo.Z >= hi.X - lo.X;
        float span = alongZ ? hi.Z - lo.Z : hi.X - lo.X;
        Vector3 along = alongZ ? Vector3.Back : Vector3.Right, side = alongZ ? Vector3.Right : Vector3.Back;
        Vector3 Ground(Vector3 v, float up) => v with { Y = _game.Zone.GroundAt(v.X, v.Z).Y + up };
        // From the ground, a long walk off its flank, three-quarters on.
        Vector3 far = Ground(mid + side * span * 0.85f + along * span * 0.45f, 1.7f);
        foreach (int x in Frame(far, mid + Vector3.Up * 4f, $"{dir}/{tag}-ground.png", 240)) yield return x;
        // From the air, over the other quarter.
        Vector3 air = mid - side * span * 0.55f - along * span * 0.5f + Vector3.Up * span * 0.42f;
        foreach (int x in Frame(air, mid, $"{dir}/{tag}-air.png", 180)) yield return x;
        // Inside: a room's far corner at eye height looking down its length, the torch on.
        _game.Torch.Visible = true;
        foreach (string purpose in new[] { "bridge", "engine-room", "hold" })
        {
            PlacedModule m = inst.Layout.Modules.Where(x => x.Purpose == purpose).OrderByDescending(x => x.Cells.Count()).FirstOrDefault();
            if (m == null) { GD.Print($"hulk: no {purpose}"); continue; }
            int floor = m.Cells.GroupBy(c => c.Y).OrderByDescending(g => g.Count()).First().Key;
            var cells = m.Cells.Where(c => c.Y == floor && !BlockInfo.Collides(inst.Blocks.Get(c).Type)).ToList();
            Vector3 c0 = new((float)cells.Average(c => c.X), floor, (float)cells.Average(c => c.Z));
            // The cell farthest from the middle along the room's long way, and look back across it.
            bool longZ = cells.Max(c => c.Z) - cells.Min(c => c.Z) >= cells.Max(c => c.X) - cells.Min(c => c.X);
            Int3 end = purpose == "bridge"
                ? cells.OrderByDescending(c => alongZ ? Math.Abs(c.Z - c0.Z) : Math.Abs(c.X - c0.X)).First()
                : cells.OrderByDescending(c => longZ ? Math.Abs(c.Z - c0.Z) : Math.Abs(c.X - c0.X)).ThenBy(c => longZ ? Math.Abs(c.X - c0.X) : Math.Abs(c.Z - c0.Z)).First();
            Vector3 eye = p.WorldOf(end, 1.65f), look = p.ToWorld(c0 + new Vector3(0.5f, purpose == "hold" ? 3f : 1.2f, 0.5f));
            if (purpose == "bridge")
            {
                // From the back of the bridge toward its windows.
                var glazed = cells.Where(c => Dirs.Horizontal.Any(h => inst.Blocks.Get(c + Int3.Up * 2 + h.Vec()).Type == BlockType.Pane)).ToList();
                Int3 window = glazed.Count > 0 ? glazed.OrderBy(c => Math.Abs(c.X - c0.X) + Math.Abs(c.Z - c0.Z)).First() : end;
                Int3 back = cells.OrderByDescending(c => c.Manhattan(window)).First();
                eye = p.WorldOf(back, 1.65f);
                look = p.WorldOf(window, 1.3f);
            }
            GD.Print($"hulk: {purpose} {m.Cells.Count()} cells, eye {eye}, look {look}");
            _game.Survivor.PlaceOutside((eye + Vector3.Down * 1.6f).ToNumerics());
            foreach (int x in _drive.Wait(60)) yield return x;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
            foreach (int x in _drive.Wait(90)) yield return x;
            foreach (int x in Shot($"{dir}/{tag}-{purpose}.png")) yield return x;
            _game.FixedCamera = null;
        }
    }
}
