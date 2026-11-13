using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PlanetSurface.Terrain;
using Slice.Construction;
using Slice.Ui;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// Heavy ships break trees (the owner's ruling): the biggest flyer let down by its pilot onto a wood ends on its gear on the
/// ground, the trees under it felled and none left in its hull, and the frames while they break keep the 16.7 ms budget; the
/// smallest flyer let down onto the nearest tree's crown ends landed there or clear of it, never held up, neither flying nor landed.
/// </summary>
public partial class VehicleGroundTest
{
    IEnumerable<int> Woods()
    {
        GD.Print("== woods ==");
        List<LibraryEntry> kinds = Kinds();
        foreach (var (e, heavy) in new[] { (_bigFlyer, true), (kinds.FirstOrDefault(k => k.Plan.Mode == CoreMode.Flight), false) })
        {
            if (e == null) continue;
            Vessel v = null;
            foreach (int x in SetDown(e, r => v = r)) yield return x;
            if (!Check(v != null && v.Flight != null && _game.Survivor.Sit(v), $"woods: the {e.Name} set down whole and seated")) continue;
            foreach (int x in LetDownOnTrees(v, heavy)) yield return x;
            _game.Survivor.StandUp();
            foreach (int x in _drive.Wait(5)) yield return x;
            _game.Survivor.PlaceOutside((v.Body.Position + N.Vector3.UnitY * 30f).ToGodot().ToNumerics());
            _game.Zone.Physics.Unregister(v.Body);
            _game.Zone.Physics.StuckFields.Remove(v.Field);
            _game.Zone.Untrack(v);
            v.Node.QueueFree();
            foreach (int x in _drive.Wait(10)) yield return x;
        }
    }

    /// <summary>The standing trees' trunks near a point: id, where the trunk stands (frame), its radius.</summary>
    List<(long Id, Vector3 At, float Radius)> TrunksNear(Vector3 p, float reach) =>
        _game.Zone.Streamer.SolidsNear(p, reach).Where(s => s.solid.Fellable)
            .Select(s => (s.solid.Id, s.solid.Center + s.offset, s.solid.Extents.X)).GroupBy(t => t.Id).Select(g => g.First()).ToList();

    /// <summary>The highest point of any crown within a reach of a point, m (the ground there when there is none).</summary>
    float CrownTop(Vector3 p, float reach)
    {
        float top = _game.Zone.GroundAt(p.X, p.Z).Y;
        foreach (var (s, off) in _game.Zone.Streamer.SolidsNear(p, reach, canopies: true))
            if (s.IsCanopy)
                foreach (N.Vector3 q in _game.Zone.SpeciesOf(s.Plant).Canopy[s.Variant][s.Part])
                    top = MathF.Max(top, (s.Center + off + s.Basis * new Vector3(q.X, q.Y, q.Z) * s.Scale).Y);
            else top = MathF.Max(top, s.Center.Y + off.Y + s.Extents.Y);
        return top;
    }

    /// <summary>A heavy ship over the nearest wood near by that it breaks; a light one over the nearest trunk with a crown. Null where this world has no trees near.</summary>
    Vector3? WoodSpot(Vessel v, bool heavy)
    {
        var play = new Playthrough(_game);
        Vector3 from = v.Body.Position.ToGodot();
        float water = _game.Zone.Planet.Source.WaterLevel + 1f;
        var trunks = TrunksNear(from, 260f).Where(t => t.At.DistanceTo(from) > 30f && _game.Zone.GroundAt(t.At.X, t.At.Z).Y > water).ToList();
        if (trunks.Count == 0) return null;
        if (!heavy)
        {
            var canopied = trunks.Where(t => _game.Zone.Streamer.SolidsNear(t.At, 0.5f, canopies: true).Any(s => s.solid.IsCanopy && s.solid.Id == t.Id)).ToList();
            return canopied.Count == 0 ? null : canopied.MaxBy(t => t.Radius).At;
        }
        // A wood it breaks: no trunk under or round it thick enough to take its weight (one that is holds it, by the same rule).
        float weight = _game.Zone.Physics.WeightOf(v.Body);
        PhysicsRules.FloraData rule = _game.Zone.Physics.Rules.Flora;
        var open = trunks.Where(t => !play.RuinAt(_game.Zone.GroundAt(t.At.X, t.At.Z), 24f) && !trunks.Any(o => o.At.DistanceTo(t.At) < 26f && rule.Takes(o.Radius) >= weight)).ToList();
        return open.Count == 0 ? null : open.MaxBy(t => trunks.Count(o => o.At.DistanceTo(t.At) < 9f)).At;
    }

    /// <summary>The standing trees whose colliders the hull's own shapes overlap.</summary>
    int TreesInHull(Vessel v)
    {
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        var q = new PhysicsShapeQueryParameters3D { CollisionMask = Layers.Flora, Margin = 0f };
        var inside = new HashSet<long>();
        foreach (Node n in v.Binding.Collider.GetChildren())
        {
            if (n is not CollisionShape3D cs || cs.Shape == null || cs.Disabled) continue;
            q.Shape = cs.Shape;
            q.Transform = cs.GlobalTransform;
            foreach (Godot.Collections.Dictionary hit in space.IntersectShape(q, 16))
                if (_game.Zone.TreeAt(((Rid)hit["rid"], (int)hit["shape"]), out long tree, out float r) && r > 0f) inside.Add(tree);
        }
        return inside.Count;
    }

    IEnumerable<int> LetDownOnTrees(Vessel v, bool heavy)
    {
        string name = v.Name;
        if (WoodSpot(v, heavy) is not Vector3 spot)
        {
            GD.Print($"  ({name}: no {(heavy ? "wood" : "crowned tree")} within 260 m on this world: nothing to let it down on)");
            yield break;
        }
        float feet = v.Body.Shape.FeetBelowOrigin, weight = _game.Zone.Physics.WeightOf(v.Body);
        float above = CrownTop(spot, 20f) + feet + 4f;
        Put(v, new N.Vector3(spot.X, above, spot.Z), MathX.Yaw(MathX.Heading(v.Body.Rotation)), N.Vector3.Zero);
        TerrainStreamer streamer = _game.Zone.Streamer;
        int wait = 0;
        for (; wait < 900 && !(streamer.SolidsReady && streamer.CollidesAt(spot)); wait++)
        {
            Put(v, new N.Vector3(spot.X, above, spot.Z), v.Body.Rotation, N.Vector3.Zero); // held there while the ground and trees under it come in
            yield return 0;
        }
        foreach (int x in _drive.Wait(30)) yield return x;
        var before = TrunksNear(spot, 30f);
        var nearest = before.OrderBy(t => new Vector2(t.At.X - spot.X, t.At.Z - spot.Z).Length()).FirstOrDefault();
        GD.Print($"  ({name}, {v.Body.Mass / 1000f:F0} t: over {before.Count} trees within 30 m, the one under it {nearest.Radius:F2} m thick (takes {_game.Zone.Physics.Rules.Flora.Takes(nearest.Radius) / 1000f:F0} kN, it weighs {weight / 1000f:F0} kN), {above - _game.Zone.GroundAt(spot.X, spot.Z).Y:F0} m up, streamed in {wait / 60f:F1} s)");

        // The pilot lets it down: Ctrl to sink, gently near the ground, and hands off once it stands on something or stops sinking.
        var probe = new PlanetSurface.FrameProbe(this);
        var cpu = new List<double>();
        double worstBreaking = 0;
        int felledAt = _game.Zone.Planet.Felled.Count, broke = 0, still = 0, t = 0;
        var c = _game.Controls;
        for (; t < 60 * 60; t++)
        {
            float agl = v.Body.Position.Y - feet - _game.Zone.GroundAt(v.Body.Position.X, v.Body.Position.Z).Y;
            float wanted = agl > 8f ? -3f : -1f, vy = v.Body.Velocity.Y;
            still = MathF.Abs(vy) < 0.2f ? still + 1 : 0;
            if (v.Controller.Frozen || v.Controller.Gear is { Attached: true } || still > 45) break;
            c.Hold(Key.Ctrl, vy > wanted + 0.5f);
            c.Hold(Key.Space, vy < 2f * wanted - 1f);
            yield return 0;
            int felled = _game.Zone.Planet.Felled.Count;
            if (broke > 0 || felled > felledAt) cpu.Add(probe.LastCpuMs);
            if (felled > felledAt) worstBreaking = Math.Max(worstBreaking, probe.LastCpuMs);
            broke += felled - felledAt;
            felledAt = felled;
        }
        c.ReleaseAll();
        int left = 0;
        for (; left < 600 && !v.Controller.Frozen; left++)
        {
            yield return 0;
            if (broke > 0) cpu.Add(probe.LastCpuMs);
        }
        probe.Remove();

        VesselController ctl = v.Controller;
        LandingGear gear = ctl.Gear;
        Vector3 at = v.Body.Position.ToGodot();
        float over = at.Y - feet - _game.Zone.GroundAt(at.X, at.Z).Y;
        var onTree = Enumerable.Range(0, gear?.Count ?? 0).Where(i => gear.Touching(i) && gear.FootTree[i] != 0).ToList();
        string how = $"frozen {ctl.Frozen} after {(t + left) / 60f:F1} s, gear attached {gear?.Attached}, {onTree.Count} feet on a tree, keel {over:F1} m over the ground, {broke} trees broken, {TreesInHull(v)} in its hull";
        if (heavy)
        {
            // The trees that stood under where it ended: every one felled.
            var (lo, hi) = v.Structure.Bounds();
            float cell = v.Structure.Grid.CellSize;
            Transform3D toLocal = v.DrawTransform.AffineInverse();
            var under = before.Where(b =>
            {
                Vector3 l = toLocal * b.At;
                return l.X > (lo.X - 0.5f) * cell && l.X < (hi.X + 0.5f) * cell && l.Z > (lo.Z - 0.5f) * cell && l.Z < (hi.Z + 0.5f) * cell;
            }).ToList();
            int standing = under.Count(b => !_game.Zone.Planet.Felled.Contains(b.Id));
            Check(ctl.Frozen && gear is { Attached: true } && onTree.Count == 0 && under.Count > 0 && standing == 0 && TreesInHull(v) == 0,
                $"{name}: let down on a wood, it ends on its gear on the ground, the {under.Count} trees under it felled ({standing} still standing; {how})");
            Check(_game.Zone.Trees.Stumps > 0, $"{name}: the trees it broke leave what harvesting leaves: {_game.Zone.Trees.Stumps} stumps, their trunks and crowns fallen");
            cpu.Sort();
            double p95 = cpu.Count == 0 ? 0 : cpu[(int)(cpu.Count * 0.95)], worst = cpu.Count == 0 ? 0 : cpu[^1];
            Check(broke > 0 && p95 < 16.7 && worstBreaking < 16.7 && worst < 33.4,
                $"{name}: the frames while the trees break keep the budget in CPU time: worst frame a tree broke in {worstBreaking:F1} ms, 95th percentile {p95:F1} ms, worst {worst:F1} ms over {cpu.Count} frames (budget 16.7; headless, machine shared)");
        }
        else
            Check(ctl.Frozen && TreesInHull(v) == 0,
                $"{name}: let down on a {nearest.Radius:F2} m tree's crown, it ends {(onTree.Count > 0 ? "landed on it, its gear on the crown" : over < 3f ? "clear of it, landed on the ground" : "landed resting in it")}, never held up neither flying nor landed ({how})");
    }
}
