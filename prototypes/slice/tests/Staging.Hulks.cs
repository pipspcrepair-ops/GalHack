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

namespace Slice.Tests;

/// <summary>
/// A crashed capital ship, a real vessel, on the world as it rolled (run with --force=crashed_freighter; HulkTest.Force
/// picks a long hauler): from the ground at a distance, its engine room inside, its stern's thruster banks, then claimed
/// with a core, filled in (Creative) and lifting off out of the ground it came down in. Frames into --dir, led by --tag.
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
        CapitalHull.HullPlan plan = CapitalHull.Of(site.Instance.Layout);
        GD.Print($"hulk: {site.Family.Name} '{site.Instance.Layout.Variant?.Name}' {site.Instance.State}");
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        for (int i = 0; i < 1200 && !_game.Hulks.Down.ContainsKey(site); i++) yield return 0;
        foreach (int x in _drive.Wait(240)) yield return x;
        Vessel v = _game.Hulks.Down[site][0];
        var (lo, hi) = v.Structure.Bounds();
        Vector3 Local(float x, float y, float z) => v.ToWorld(new N.Vector3(x, y, z));
        Vector3 mid = Local(lo.X + hi.X, lo.Y + hi.Y, lo.Z + hi.Z); // the middle of the blocks' bounds: (lo + hi) / 2 cells of 2 m
        float span = (hi.Z - lo.Z) * 2f;
        Vector3 Ground(Vector3 p, float up) => p with { Y = _game.Zone.GroundAt(p.X, p.Z).Y + up };
        float sea = _game.Zone.Planet.Source.WaterLevel;

        // From the ground, off its flank, three-quarters on: dry land with the hull in sight.
        Vector3 along = (Local(0, 0, 10) - Local(0, 0, 0)).Normalized(), side = along.Cross(Vector3.Up).Normalized();
        Vector3 far = Ground(mid + side * span * 0.8f + along * span * 0.35f, 1.7f);
        float best = float.MinValue;
        for (int k = 0; k < 24; k++)
            foreach (float r in new[] { 0.65f, 0.85f, 1.05f })
            {
                float a = k * MathF.Tau / 24f;
                Vector3 c = Ground(mid + (side * MathF.Cos(a) + along * MathF.Sin(a)) * span * r, 1.7f);
                if (c.Y - 1.7f < sea + 0.5f || _game.Zone.Streamer.FloraNear(c, 4f).Any()) continue;
                bool hidden = false;
                for (float s = 0.05f; s < 0.7f && !hidden; s += 0.05f)
                {
                    Vector3 q = c.Lerp(mid + Vector3.Up * 6f, s);
                    hidden = _game.Zone.GroundAt(q.X, q.Z).Y > q.Y - 0.5f;
                }
                if (hidden) continue;
                float score = MathF.Abs(MathF.Cos(a)) * 2f - MathF.Abs(MathF.Sin(a) - 0.5f) - r;
                if (score > best) { best = score; far = c; }
            }
        foreach (int x in Frame(far, mid + Vector3.Up * 6f, $"{dir}/{tag}-ground.png", 240)) yield return x;

        // Inside its engine room: down the aisle to the drive machinery against the stern bulkhead, the torch on.
        // The stern's thrusters stand on nacelles four blocks deep; the engine room's back wall is just inside them.
        int aft = v.Structure.Blocks().Where(b => v.Structure.TypeOf(b.block).Id == "thruster").Select(b => b.pos.Z).DefaultIfEmpty(hi.Z).Max();
        int back = aft - 6;
        Vector3 eye = Local(0f, 3.4f, (back - 8) * 2f), look = Local(0f, 3f, back * 2f);
        _game.Survivor.PlaceOutside((eye + Vector3.Down * 1.6f - (look - eye).Normalized() * 1.2f).ToNumerics());
        _game.Torch.Visible = true;
        foreach (int x in _drive.Wait(60)) yield return x;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(90)) yield return x;
        foreach (int x in Shot($"{dir}/{tag}-engine-room.png")) yield return x;
        _game.FixedCamera = null;
        _game.Torch.Visible = false;

        // Its stern: the thruster banks on the nacelles.
        Vector3 stern = Local(0f, 9f, hi.Z * 2f);
        Vector3 from = Ground(stern + along * 34f + side * 20f, 1.7f) + Vector3.Up * 6f;
        foreach (int x in Frame(from, stern, $"{dir}/{tag}-stern.png", 120)) yield return x;

        // Claimed, filled in, lifted off: the ground it lay in cut and crumbling, the hull rising out of its bed.
        CapitalHull.HullPiece piece = plan.Pieces[0];
        string why = piece.CoreSlot is PoiGenerator.Gen.Int3 slot ? v.ClaimWith(new GalHack.Structures.Int3(slot.X, slot.Y, slot.Z)) : "no seat";
        int put = HulkTest.Fill(v, piece, _game.Zone.Data.Catalogue);
        v.SwitchMode(CoreMode.Flight);
        foreach (int x in _drive.Wait(30)) yield return x;
        // Watched from the ground off its flank, low, where its keel leaves the ground it lay in.
        float beam = (hi.X - lo.X) * 2f;
        Vector3 watch = Ground(mid + side * (beam * 0.5f + 46f) - along * span * 0.1f, 4f);
        for (int k = 0; k < 12 && _game.Zone.Streamer.FloraNear(watch, 4f).Any(); k++) watch = Ground(watch + along * 6f, 4f);
        _game.Survivor.PlaceOutside((watch + side * 4f + Vector3.Down * 1.5f).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        var cut = v.LiftOff();
        GD.Print($"hulk: claimed {(why ?? "ok")}, {put} blocks put back, cut {cut.Cells.Count} cells, climb {cut.Climb:F1} m {(cut.Ok ? "" : cut.Refusal)}");
        Vector3 keel = Local(lo.X + hi.X, lo.Y * 2f, lo.Z + hi.Z) + side * beam * 0.5f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(keel + Vector3.Up * 3f - watch, Vector3.Up), watch);
        for (int i = 0; i < 900 && v.Lifting != null && v.Lifting.Climbed < 3f; i++) yield return 0;
        foreach (int x in Shot($"{dir}/{tag}-liftoff.png")) yield return x;
        // Clear of the cut, its pilot keeps it climbing: up and away out of the bed it lay in.
        for (int i = 0; i < 900 && v.Lifting != null && v.Lifting.Phase != CapitalBase.World.LiftOffPhase.Done; i++) yield return 0;
        for (int i = 0; i < 300; i++)
        {
            v.Flight.Input = ShipHandling.Handling.PilotInput.Hands(MathX.Unrotate(v.Body.Rotation, N.Vector3.UnitY * 0.04f).ToGodot());
            yield return 0;
        }
        foreach (int x in Shot($"{dir}/{tag}-airborne.png")) yield return x;
        _game.FixedCamera = null;
    }
}
