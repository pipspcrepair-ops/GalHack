using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Arms;
using GalHack.Physics.Engine;
using Godot;
using PoiGenerator.Gen;
using Slice.Arms;
using Slice.Places;
using Slice.Player;
using Slice.Surface;
using Wildlife;

namespace Slice.Tests;

/// <summary>Firing at the world (tests and the shotdamage view): a place's outside wall to aim at, a shot fired at a point.</summary>
public static class WorldFire
{
    /// <summary>A wall block on a place's outside at chest height with open air 8 m before it: the cell, and the way out of the wall.</summary>
    public static bool Wall(Place p, Random rng, out Int3 cell, out Int3 outward)
    {
        BlockGrid g = p.Inst.Blocks;
        CellMask occupied = p.Inst.Layout.Occupied;
        bool Open(Int3 c) => !g.InBounds(c) || g.Get(c).IsAir && !occupied[c];
        var found = new List<(Int3, Int3)>();
        foreach (var (c, b) in g.Blocks())
        {
            if (b.Role != PoiGenerator.Gen.Role.Wall || c.Y != Assembler.GroundY + 2 || b.Type is BlockType.Door or BlockType.Pane) continue;
            foreach (Dir d in Dirs.Horizontal)
                if (Enumerable.Range(1, 8).All(k => Open(Out(c, d.Vec(), k)) && Open(Out(c, d.Vec(), k) + Int3.Down))) found.Add((c, d.Vec()));
        }
        static Int3 Out(Int3 c, Int3 v, int k) => new(c.X + v.X * k, c.Y + v.Y * k, c.Z + v.Z * k);
        (cell, outward) = found.Count > 0 ? found[rng.Next(found.Count)] : default;
        return found.Count > 0;
    }

    /// <summary>A shot of a row fired by no one in particular (an NPC's shot) from a point at another.</summary>
    public static void Fire(Game.Game game, WeaponDef d, Vector3 from, Vector3 at) => game.Arms.FireFrom(d, from, at - from, 0f, null);

    /// <summary>The heaviest blast a row has (a rocket).</summary>
    public static WeaponDef Rocket(Game.Game game) => game.Zone.Data.Arms.All.Where(d => d.Blast != null && d.Placed == null).OrderByDescending(d => d.Blast.Damage * d.Blast.Blocks).First();

    /// <summary>Where a ray straight down meets the world's colliders, or null.</summary>
    public static float? Down(Game.Game game, Vector3 over)
    {
        var hit = game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(over + Vector3.Up * 6f, over + Vector3.Down * 12f, GalHack.Physics.Layers.World));
        return hit.Count > 0 ? hit["position"].AsVector3().Y : null;
    }
}

public partial class CombatTest
{
    /// <summary>
    /// Weapons damage the world (owner, 17 Nov 2026: "So they can damage anything, really"): an NPC's rounds chip a place's wall, a
    /// rocket breaks it open and leaves nothing hanging, a blast on open ground craters it and the crater is there after its chunks
    /// went and came back, a shot wears a tree and a blast fells it, and a firefight's frames stay in budget.
    /// </summary>
    IEnumerable<int> World(Game.Game game)
    {
        GD.Print("-- weapons on the world --");
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        ArmsHost arms = game.Arms;
        WeaponDef rifle = game.Zone.Data.Arms.OfItem(Weapons.ItemOf("rifle")), rocket = WorldFire.Rocket(game);
        Vector3 shift = Vector3.Zero;
        game.Zone.FrameShifted += by => shift += by;
        IEnumerable<int> Tree(PlanetSurface.Terrain.TerrainStreamer.PlantSighting tree)
        {
            Vector3 trunk = tree.Root + Vector3.Up * 1.2f, side = (game.Survivor.WorldFeet - tree.Root) with { Y = 0f };
            side = side.LengthSquared() > 0.01f ? side.Normalized() : Vector3.Right;
            game.Survivor.PlaceOutside((tree.Root + side * 10f + Vector3.Up).ToNumerics());
            foreach (int x in Hold(90)) yield return x;
            float whole = game.Zone.PlantMax(tree);
            int plantHits = arms.PlantHits;
            for (int i = 0; i < 2; i++)
            {
                WorldFire.Fire(game, rifle, trunk + side * 5f, trunk);
                foreach (int x in Hold(10)) yield return x;
            }
            float left = game.Zone.PlantHealth(tree).Hp;
            Check(arms.PlantHits > plantHits && left < whole && !game.Zone.Planet.Felled.Contains(tree.Id), $"rifle rounds wear a tree: {left:F1} of {whole:F1} hit points left, standing");
            WorldFire.Fire(game, rocket, trunk + side * 6f, tree.Root + Vector3.Up * 0.3f);
            foreach (int x in Hold(60)) yield return x;
            Check(game.Zone.Planet.Felled.Contains(tree.Id), $"a blast at its foot fells it ({arms.PlantsBlasted} plants a blast took so far)");
        }
        IEnumerable<int> Hold(int ticks) { for (int i = 0; i < ticks; i++) { game.Suit.Vitals.Health = max; yield return 0; } }
        Place place = game.Places.Standing.Where(p => p.Ready && WorldFire.Wall(p, _rng, out _, out _)).OrderBy(p => p.WorldOf(p.Inst.Blocks.Min).DistanceTo(game.Survivor.WorldFeet)).FirstOrDefault();
        Check(place != null, $"a place stands with an outside wall to fire at ({game.Places.Standing.Count} standing)");
        if (place == null) yield break;
        WorldFire.Wall(place, _rng, out Int3 cell, out Int3 o);
        Vector3 outward = new(o.X, o.Y, o.Z), face = place.WorldOf(cell) + outward * 0.5f;
        game.Survivor.PlaceOutside((face + outward * 14f + outward.Cross(Vector3.Up) * 6f).ToNumerics());
        foreach (int x in Hold(90)) yield return x;
        // An NPC's rounds: the wall is chipped.
        int placeHits = arms.PlaceHits;
        for (int i = 0; i < 6; i++)
        {
            WorldFire.Fire(game, rifle, face + outward * 6f, face);
            foreach (int x in Hold(8)) yield return x;
        }
        float chip = place.Memory.BlockDamage.Where(kv => place.WorldOf(kv.Key).DistanceTo(face) < 2f).Sum(kv => kv.Value);
        Check(arms.PlaceHits > placeHits && chip > 0f, $"an NPC's rifle rounds chip a place's wall: {arms.PlaceHits - placeHits} of 6 into it, {chip:F0} hit points off its blocks ({place.Inst.Blocks.Get(cell).Material} {BlockInfo.HitPoints(place.Inst.Blocks.Get(cell))})");
        // A rocket: blocks broken, nothing left hanging, rebuilt within a few frames.
        BlockGrid g = place.Inst.Blocks;
        var anchors = place.Inst.Layout.Extras.InSpace ? LayoutCheck.StationAnchors(place.Inst.Layout) : null;
        int floating = PoiGenerator.Gen.Structure.Floating(g, place.Inst.Layout.Occupied, anchors).Count, broken = game.Places.BlocksBroken, fallen = game.Places.BlocksFallen;
        for (int i = 0; i < 3; i++)
        {
            WorldFire.Fire(game, rocket, face + outward * 7f, face);
            foreach (int x in Hold(40)) yield return x;
        }
        foreach (int x in Hold(30)) yield return x;
        int after = PoiGenerator.Gen.Structure.Floating(g, place.Inst.Layout.Occupied, anchors).Count;
        Check(game.Places.BlocksBroken > broken && after <= floating && place.Stale == 0,
            $"three of the heaviest blast ({rocket.Id}, {rocket.Blast.Damage * rocket.Blast.Blocks:F0} to blocks over {rocket.Blast.Radius} m) on the wall: {game.Places.BlocksBroken - broken} blocks broken, {game.Places.BlocksFallen - fallen} gone for want of support, floating blocks {floating} before and {after} after, {place.Stale} chunks still to rebuild");

        // Open ground: a crater, there after its chunks go and come back.
        Vector3 feet = game.Survivor.WorldFeet, spot = default;
        // (Rings out from 45 m: a big place round the wall leaves no open ground that near on some worlds.)
        for (int k = 0; k < 96 && spot == default; k++)
        {
            float a = k * MathF.Tau / 24f, ring = 45f + 30f * (k / 24);
            Vector3 p = game.Zone.GroundAt(feet.X + MathF.Cos(a) * ring, feet.Z + MathF.Sin(a) * ring);
            if (game.Places.PlaceAt(p) == null && game.Places.PlaceAt(p + Vector3.Up * 3f) == null && game.Zone.Vessels.All(v => v.Body.Position.ToGodot().DistanceTo(p) > 20f)) spot = p;
        }
        Check(spot != default, $"open ground {(spot == default ? 0f : spot.DistanceTo(feet)):F0} m off, clear of places and structures");
        if (spot == default) yield break;
        game.Survivor.PlaceOutside((spot + new Vector3(12f, 1f, 0f)).ToNumerics());
        foreach (int x in Hold(120)) yield return x;
        float? before = WorldFire.Down(game, spot);
        int craters = arms.Craters;
        WorldFire.Fire(game, rocket, spot + new Vector3(3f, 6f, 0f), spot);
        foreach (int x in Hold(90)) yield return x;
        float? dug = WorldFire.Down(game, spot + shift);
        spot += shift;
        shift = Vector3.Zero;
        game.Survivor.PlaceOutside((spot + new Vector3(1500f, 0f, 0f) with { Y = game.Zone.GroundAt(spot.X + 1500f, spot.Z).Y + 1f }).ToNumerics());
        for (int i = 0; i < 1200 && (i < 120 || !game.Zone.Streamer.Settled); i++) { game.Suit.Vitals.Health = max; yield return 0; }
        float? away = WorldFire.Down(game, spot + shift);
        game.Survivor.PlaceOutside((spot + shift + new Vector3(12f, 1f, 0f)).ToNumerics());
        for (int i = 0; i < 1200 && (i < 120 || !game.Zone.Streamer.Settled); i++) { game.Suit.Vitals.Health = max; yield return 0; }
        float? back = WorldFire.Down(game, spot + shift);
        Check(arms.Craters > craters && before is float b0 && dug < b0 - 0.4f && back < b0 - 0.4f && MathF.Abs(back.Value - dug.Value) < 0.3f,
            $"a blast on open ground craters it: the ground at {before:F2} m, {dug:F2} after, gone with its chunk 1.5 km off ({(away == null ? "no collider" : $"{away:F2}")}), {back:F2} back again");

        // A tree: a shot wears it, a blast fells it.
        feet = game.Survivor.WorldFeet;
        var tree = game.Zone.Streamer.FloraNear(feet, 250f).Where(s => TreeFx.IsTree(s.Kind) && s.Variant >= 0 && !game.Zone.Planet.Felled.Contains(s.Id) && game.Places.PlaceAt(s.Root) == null)
            .OrderBy(s => s.Root.DistanceTo(feet)).FirstOrDefault();
        if (tree.Id == 0) GD.Print("  (no tree within 250 m on this world: the tree's checks wait for another)");
        else foreach (int x in Tree(tree)) yield return x;
        // A firefight's cost: rounds and rockets into the place, then into open ground, without the harm and with it.
        var split = new FrameSplit(GetTree().Root, GetViewport().GetViewportRid()) { HitchMs = 11 };
        split.Systems = new SystemSplit(game, game.Zone, game.Places);
        split.Doing = () => { ulong f = Engine.GetProcessFrames(); return $"prof [{PlanetSurface.Prof.Of(f - 1)}] [{PlanetSurface.Prof.Of(f)}]"; };
        PlanetSurface.Prof.On = true;
        GalHack.Testing.AllocMeter.On = true;
        var pacings = new List<string>();
        game.GetNode<Slice.Game.GcPacer>("GC pacer").Collected = (mb, ms) => pacings.Add($"{mb:F0}MB {ms:F1}ms (gen1 #{GC.CollectionCount(1)})");
        foreach (var (where, at, from) in new[] { ("at the place", face, outward), ("in the open", spot + shift, new Vector3(0f, 0.5f, 1f).Normalized()) })
        {
            game.Survivor.PlaceOutside((at + from * 16f + from.Cross(Vector3.Up) * 8f + Vector3.Up).ToNumerics());
            foreach (int x in Hold(120)) yield return x;
            var cost = new List<double>[2];
            for (int pass = 0; pass < 2; pass++)
            {
                ArmsHost.HarmsWorld = pass == 1;
                ArmsHost.PlacesMs = ArmsHost.PlantsMs = ArmsHost.CraterMs = 0;
                split.Systems.Reset();
                split.Reset();
                PlanetSurface.Prof.TakeWorst();
                GalHack.Testing.AllocMeter.Take(1);
                int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                TimeSpan paused = GC.GetTotalPauseDuration();
                long all = GC.GetTotalAllocatedBytes(false);
                var seconds = new List<string>();
                double lastP = -1, lastPh = -1;
                cost[pass] = new List<double>();
                for (int i = 0; i < 240; i++)
                {
                    Vector3 jitter = new((float)_rng.NextDouble() * 4f - 2f, (float)_rng.NextDouble() * 2f - 0.5f, (float)_rng.NextDouble() * 4f - 2f);
                    WorldFire.Fire(game, rifle, at + from * 8f + jitter, at + jitter * 0.5f);
                    if (i % 30 == 0) WorldFire.Fire(game, rocket, at + from * 9f + jitter, at + jitter * 0.6f);
                    game.Suit.Vitals.Health = max;
                    yield return 0;
                    double pr = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0, ph = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
                    if (pr != lastP || ph != lastPh) seconds.Add($"{pr:F1}+{ph:F1}@{i}");
                    (lastP, lastPh) = (pr, ph);
                    cost[pass].Add(pr + ph);
                }
                GD.Print($"  PROF {where} harm={pass}: seconds (process+physics worst) {string.Join(" ", seconds)}; GC {GC.CollectionCount(0) - g0}/{GC.CollectionCount(1) - g1}/{GC.CollectionCount(2) - g2} pause {(GC.GetTotalPauseDuration() - paused).TotalMilliseconds:F1} ms, all threads {(GC.GetTotalAllocatedBytes(false) - all) / 1048576.0:F1} MB in 4 s; workers MB/s: {GalHack.Testing.AllocMeter.Take(4)}");
                foreach (GCKind kind in new[] { GCKind.Ephemeral, GCKind.FullBlocking, GCKind.Background })
                {
                    GCMemoryInfo gi = GC.GetGCMemoryInfo(kind);
                    if (gi.Index == 0) continue;
                    var gens = gi.GenerationInfo.ToArray();
                    GD.Print($"  PROF last {kind} GC #{gi.Index} gen {gi.Generation}: pause {(gi.PauseDurations.Length > 0 ? gi.PauseDurations[0].TotalMilliseconds : 0):F1} ms, promoted {gi.PromotedBytes / 1048576.0:F1} MB, heap {gi.HeapSizeBytes / 1048576.0:F0} MB, gens before/after MB {string.Join(" ", Enumerable.Range(0, gens.Length).Select(g => $"{gens[g].SizeBeforeBytes / 1048576.0:F1}/{gens[g].SizeAfterBytes / 1048576.0:F1}"))}, pinned {gi.PinnedObjectsCount}, handles?");
                }
                GD.Print($"  PROF live shots {arms.Pool.Live}: {string.Join(", ", arms.Pool.Shots.Where(s => s.Live).GroupBy(s => $"{s.Def.Id} {s.State}").Select(g => $"{g.Key} x{g.Count()} age {g.Average(s => s.Age):F1} off {g.Average(s => s.Pos.ToGodot().DistanceTo(at)):F0} m, {g.Count(s => s.Pos.Y < game.Zone.GroundAt(s.Pos.X, s.Pos.Z).Y)} under the ground, {g.Count(s => game.Places.PlaceAt(s.Pos.ToGodot()) != null)} in a place, height over ground {g.Average(s => s.Pos.Y - game.Zone.GroundAt(s.Pos.X, s.Pos.Z).Y):F1}"))}; solid paths air/novox/graze/voxhit/all {string.Join("/", ArmsHost.SolidPaths)}");
                Array.Clear(ArmsHost.SolidPaths);
                GD.Print($"  PROF pacer: {string.Join(", ", pacings)}");
                pacings.Clear();
                GD.Print($"  PROF frames: {split.Report()}");
                GD.Print($"  PROF systems: {split.Systems.Report()}");
                foreach (string h in split.Hitches.OrderByDescending(h => double.Parse(h.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture)).Take(8)) GD.Print($"  PROF hitch {h}");
                GD.Print($"  PROF worst spans a frame: {PlanetSurface.Prof.TakeWorst()}");
                foreach (int x in Hold(60)) yield return x;
            }
            ArmsHost.HarmsWorld = true;
            double P95(List<double> l) => l.OrderBy(v => v).ElementAt((int)(l.Count * 0.95));
            // (In budget, or no worse than the same fight without the harm: the frame a second's worst is the fight's own and the collector's too.)
            Check(P95(cost[1]) <= Math.Max(16.7, P95(cost[0]) + 2.0), $"a firefight {where} (a round a frame, a rocket every half second, 4 s): the worst frame a second, p95 {P95(cost[0]):F1} ms and worst {cost[0].Max():F1} without harm to the world, {P95(cost[1]):F1} and {cost[1].Max():F1} with it (budget 16.7, or 2 ms over the fight without; a blast's harm at most {ArmsHost.PlacesMs:F1} ms to places, {ArmsHost.PlantsMs:F1} to plants, {ArmsHost.CraterMs:F1} to the ground)");
        }
    }
}
