using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Places;
using Slice.Vessels;
using N = System.Numerics;
using Int3 = GalHack.Structures.Int3;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// A crashed capital ship, a real vessel, on the world as it rolled (HulkTest.Force: a crashed freighter near the start,
/// any of its poses, or --variant=N): from the ground at a distance, then claimed with a core so its lights come on, a
/// dressed room and a corridor inside. "derelict-hull" does the same for a derelict capital ship in orbit, flown up to
/// in the Skiff. Frames into --dir, led by --tag; --room names the room to look into.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> HulkView()
    {
        string dir = _args.GetValueOrDefault("dir", "."), tag = _args.GetValueOrDefault("tag", "hulk");
        System.IO.Directory.CreateDirectory(dir);
        _game.Zone.Wildlife.Frozen = true;
        _game.Visor.Visible = false;
        PoiSite site = HulkTest.SiteOf(_game);
        GD.Print($"hulk: {site.Family.Name} '{site.Instance.Layout.Variant?.Name}' {site.Instance.State}");
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        for (int i = 0; i < 3600 && !_game.Hulks.Down.ContainsKey(site); i++) yield return 0;
        foreach (int x in _drive.Wait(240)) yield return x;
        List<Vessel> wrecks = _game.Hulks.Down[site];
        Vessel v = wrecks.OrderByDescending(w => w.Structure.Count).First();
        var (lo, hi) = v.Structure.Bounds();
        Vector3 Local(float x, float y, float z) => v.ToWorld(new N.Vector3(x, y, z));
        Vector3 mid = Local((lo.X + hi.X) * 0.5f, (lo.Y + hi.Y) * 0.5f, (lo.Z + hi.Z) * 0.5f);
        float span = hi.Z - lo.Z;
        Vector3 Ground(Vector3 p, float up) => p with { Y = _game.Zone.GroundAt(p.X, p.Z).Y + up };
        float sea = _game.Zone.Planet.Source.WaterLevel;

        // From a rise off its bow quarter, three-quarters on (the taper, the shoulders, the tower and the engines read best), dry land, the hull in sight.
        Vector3 along = (Local(0, 0, 10) - Local(0, 0, 0)).Normalized(), side = along.Cross(Vector3.Up).Normalized();
        float rise = MathF.Max(12f, span * 0.12f);
        Vector3 far = Ground(mid + side * span * 0.8f - along * span * 0.35f, rise);
        float best = float.MinValue;
        for (int k = 0; k < 24; k++)
            foreach (float r in new[] { 0.8f, 1.0f, 1.2f })
            {
                float a = k * MathF.Tau / 24f;
                Vector3 c = Ground(mid + (side * MathF.Cos(a) + along * MathF.Sin(a)) * span * r, rise);
                if (c.Y - rise < sea + 0.5f || _game.Zone.Streamer.FloraNear(c, 4f).Any()) continue;
                bool hidden = false;
                for (float s = 0.05f; s < 0.7f && !hidden; s += 0.05f)
                {
                    Vector3 q = c.Lerp(mid, s);
                    hidden = _game.Zone.GroundAt(q.X, q.Z).Y > q.Y - 0.5f;
                }
                if (hidden) continue;
                float score = MathF.Abs(MathF.Cos(a)) - MathF.Abs(MathF.Sin(a) + 0.5f) - r;
                if (score > best) { best = score; far = c; }
            }
        foreach (int x in Frame(far, mid, $"{dir}/{tag}-outside.png", 240)) yield return x;

        // Claimed with a core: its generators run the lights, each room lit for its purpose.
        Vessel owned = wrecks.FirstOrDefault(w => w.Derelict.Hull?.CoreSlot != null) ?? v;
        string why = owned.Derelict.Hull?.CoreSlot is Int3 slot ? owned.ClaimWith(slot) : "no seat";
        owned.LightsOn = true;
        GD.Print($"hulk: claimed {(why ?? "ok")}, {owned.Decos.Count} pieces of dressing, {owned.Structure.Count} blocks");
        foreach (int x in Inside(owned, dir, tag)) yield return x;
    }

    /// <summary>A derelict capital ship in orbit: flown up to in the Skiff, from outside, then claimed and lit, a dressed room and a corridor.</summary>
    IEnumerable<int> DerelictHullView()
    {
        string dir = _args.GetValueOrDefault("dir", "."), tag = _args.GetValueOrDefault("tag", "derelict");
        System.IO.Directory.CreateDirectory(dir);
        foreach (int x in ToOrbit()) yield return x;
        for (int i = 0; i < 1200 && _game.Orbit is { Ready: false }; i++) yield return 0;
        Vessel ship = _game.Orbit?.Wrecks.Where(w => w.Derelict?.Hull != null).OrderByDescending(w => w.Structure.Count).FirstOrDefault();
        if (ship == null)
        {
            GD.PrintErr("derelict: no derelict capital ship in this system");
            yield break;
        }
        _game.Visor.Visible = false;
        var (lo, hi) = ship.Structure.Bounds();
        // (The orbit zone stands in the scene where its frame is laid out: a fixed camera wants the scene's own points.)
        Vector3 Local(float x, float y, float z) => ship.Zone.ToGlobal(ship.ToWorld(new N.Vector3(x, y, z)));
        Vector3 mid = Local((lo.X + hi.X) * 0.5f, (lo.Y + hi.Y) * 0.5f, (lo.Z + hi.Z) * 0.5f);
        float span = hi.Z - lo.Z;
        Vector3 eye = Local(hi.X + span * 0.6f, hi.Y + span * 0.3f, lo.Z - span * 0.3f);
        GD.Print($"derelict: {ship.Name}, {ship.Structure.Count} blocks, {span:F0} m long, {ship.Decos.Count} pieces of dressing");
        _game.FixedCamera = new Transform3D(Basis.LookingAt(mid - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(240)) yield return x;
        foreach (int x in Shot($"{dir}/{tag}-outside.png")) yield return x;
        _game.FixedCamera = null;
        string why = ship.Derelict.Hull.CoreSlot is Int3 slot ? ship.ClaimWith(slot) : "no seat";
        ship.LightsOn = true;
        GD.Print($"derelict: claimed {(why ?? "ok")}");
        foreach (int x in Inside(ship, dir, tag)) yield return x;
    }

    /// <summary>Inside a wreck: its best-dressed room of a kind people lived or worked in, then a long run of a deck's spine, each from eye height.</summary>
    IEnumerable<int> Inside(Vessel v, string dir, string tag)
    {
        HullVessel.Result hull = v.Derelict.Hull;
        Structure s = v.Structure;
        var cells = v.Decos.SelectMany(d => d.Cells).ToHashSet();
        int Dressed(HullVessel.Space sp) => sp.Floor.Count(f => cells.Contains(new Int3(f.U, sp.Y, f.V - hull.Piece.V0)));
        // A point of a space at a height over its floor, in the world (hull frame u and v, metres).
        Vector3 At(HullVessel.Space sp, float u, float v0, float up) => v.Zone.ToGlobal(v.ToWorld(new N.Vector3(u, sp.Y - 0.5f + up, v0 - hull.Piece.V0) * s.Grid.CellSize));
        string want = _args.GetValueOrDefault("room", "");
        HullVessel.Space room = hull.Spaces.Where(sp => !sp.Corridor && !sp.Hall && sp.Floor.Count >= 20 && (want == "" || sp.Purpose == want))
            .OrderBy(sp => Array.IndexOf(new[] { "galley", "medbay", "crew-quarters", "officers", "bridge" }, sp.Purpose) is int i && i >= 0 ? i : 9).ThenByDescending(Dressed).FirstOrDefault();
        if (room != null)
        {
            // From the door's side of the room, at eye height, toward its far side.
            (int U, int V) door = room.Doors.Count > 0 ? room.Doors[0].Inside : (room.U0, room.V0);
            float cu = (room.U0 + room.U1) / 2f, cv = (room.V0 + room.V1) / 2f;
            Vector3 eye = At(room, door.U + (cu - door.U) * 0.1f, door.V + (cv - door.V) * 0.1f, 1.7f);
            Vector3 look = At(room, cu + (cu - door.U) * 0.6f, cv + (cv - door.V) * 0.6f, 1.0f);
            GD.Print($"inside: {room.Purpose}, {room.Floor.Count} m², {Dressed(room)} cells dressed");
            foreach (int x in InsideShot(eye, look, $"{dir}/{tag}-room.png")) yield return x;
        }
        // Down the spine of a deck, from a third of the way along it.
        HullVessel.Space corridor = hull.Spaces.Where(sp => sp.Purpose == "corridor" && sp.Floor.Count(f => f.U == 0) > 30).OrderBy(sp => Math.Abs(sp.Deck - 2)).FirstOrDefault();
        if (corridor != null)
        {
            var spine = corridor.Floor.Where(f => f.U == 0).Select(f => f.V).OrderBy(z => z).ToList();
            int start = spine[spine.Count / 3];
            foreach (int x in InsideShot(At(corridor, 0, start, 1.7f), At(corridor, 0, start + 30, 1.3f), $"{dir}/{tag}-corridor.png")) yield return x;
        }
    }

    IEnumerable<int> InsideShot(Vector3 eye, Vector3 look, string file)
    {
        _game.Torch.Visible = true;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(120)) yield return x;
        foreach (int x in Shot(file)) yield return x;
        _game.FixedCamera = null;
        _game.Torch.Visible = false;
    }
}
