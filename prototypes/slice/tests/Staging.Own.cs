using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Slice.Surface;
using Wildlife;
using WeatherKind = Weather.Model.WeatherKind;
using ModuleKind = PoiGenerator.Gen.ModuleKind;
using Place = Slice.Places.Place;
using PoiSite = PlanetSurface.Poi.PoiSite;
using PoiState = PoiGenerator.Gen.PoiState;

namespace Slice.Tests;

public sealed partial class Staging
{
    /// <summary>
    /// --view=own [--kind=name]: the world's own kinds (what its fall, theme and era left walking it),
    /// by day, at one of their haunts: the nearest place of the kind's ground (a ruin, a living place)
    /// behind them. STAGING: one group of the kind is put down 16 m off between you and the place
    /// (printed); then it is left to go about it for a moment. Frame: own.png (or --out).
    /// </summary>
    IEnumerable<int> OwnView()
    {
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        WildlifePatch patch = _game.Zone.Wildlife;
        WorldLife life = patch.Life;
        WorldSignature sig = life.Signature;
        GD.Print($"(the world: fall {life.World.Fall} [{life.World.FallGroup}], theme {life.World.Theme}, era {life.World.Before}/{life.World.Now}; its own: {string.Join(", ", sig.Kinds.Select(k => $"{k.Species.CommonName} x{k.Weight:F1}"))})");
        if (sig.Empty) throw new InvalidOperationException("this world has no kinds of its own");
        string want = _args.GetValueOrDefault("kind", "");
        var kind = sig.Kinds.OrderByDescending(k => want != "" && k.Species.CommonName.Contains(want, StringComparison.OrdinalIgnoreCase) ? 1 : 0).ThenByDescending(k => k.Weight).First();
        Species s = kind.Species;
        Vector3 from = _game.Survivor.WorldFeet;
        // A ruin nobody lives in (a lived-in place keeps the wild out of its streets, and its own too).
        PoiSite site = _game.Zone.Planet.Pois.Where(p => p.Instance != null && p != _game.Bunker.Poi && p.Instance.State != PoiState.Active && p.Instance.Townsfolk == null && !p.Family.Tags.Contains("city"))
            .OrderBy(p => Math.Abs(_game.Places.DistanceTo(p, from) - 250f)).FirstOrDefault()
            ?? _game.Zone.Planet.Pois.Where(p => p.Instance != null && p != _game.Bunker.Poi).OrderBy(p => _game.Places.DistanceTo(p, from)).First();
        Vector3 gate = Gate(site);
        PoiGenerator.Gen.Int3 f = PoiGenerator.Gen.Dirs.Vec(site.Instance.Layout.EntranceFacing);
        Vector3 outward = new Vector3(f.X, 0f, f.Z);
        // Open ground before the place: not on any place's ground, nothing overhead, not much higher or lower than its gate.
        Vector3 stand = Ground(gate + outward * (float)Num("off", 42));
        for (int k = 0; k < 24; k++)
        {
            Vector3 c = Ground(gate + outward.Rotated(Vector3.Up, (k % 8 - 3.5f) * 0.25f) * ((float)Num("off", 42) + 8f * (k / 8)));
            if (patch.OnPlace(patch.PatchLocal(c), 4f) || MathF.Abs(c.Y - gate.Y) > 6f) continue;
            stand = c;
            break;
        }
        foreach (int x in GoTo(site, stand + Vector3.Up * 0.2f)) yield return x;
        var sky = _game.Zone.GetWorld3D().DirectSpaceState;
        for (int k = 0; k < 16 && sky.IntersectRay(PhysicsRayQueryParameters3D.Create(_game.Survivor.WorldFeet + Vector3.Up * 2f, _game.Survivor.WorldFeet + Vector3.Up * 40f)).Count > 0; k++)
        {
            stand = Ground(stand + outward * 6f);
            _game.Survivor.PlaceOutside((stand + Vector3.Up * 0.2f).ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
        }
        foreach (int x in Daylight()) yield return x;
        // Clear what else is round here, so the frame is theirs.
        Vector3 feet = _game.Survivor.WorldFeet;
        foreach (Creature c in patch.Sim.Creatures) if (patch.WorldOf(c).DistanceTo(feet) < 60f) c.Removed = true;
        // STAGING: the castaway out of their notice for the frame, so they go about it as unwatched (neither fleeing nor coming at the eye).
        patch.Sim.Targets.Remove(patch.Player);
        GD.Print("(STAGING: the castaway unnoticed for the frame)");
        int count = Math.Max(s.GroupMin, (s.GroupMin + s.GroupMax + 1) / 2);
        // Toward the place first; where that ground won't take them (a wall's footprint, water), round you till it does.
        // The wary are framed from further off, before they take fright.
        float near = (float)Num("near", s.Stats.FleeRadius > 0f ? 19 : 9);
        List<Creature> group = new();
        Vector3 mid = feet;
        for (int k = 0; k < 36 && group.Count == 0; k++)
        {
            float r = near * (1f + 0.3f * (k / 12)), a = (k % 12) * MathF.Tau / 12f * (k % 2 == 0 ? 1f : -1f);
            mid = Ground(feet - outward.Rotated(Vector3.Up, a) * r);
            group = patch.Sim.SpawnGroup(s, patch.PatchLocal(mid), count);
        }
        patch.Sim.AssignRest(group);
        GD.Print($"(STAGING: {group.Count} of {s.CommonName} put down {mid.DistanceTo(feet):F0} m off, with {site.Name} [{site.Family.Id}, {site.Instance.State}] about)");
        foreach (int x in _drive.Wait((int)Num("settle", 45))) yield return x;
        Vector3 at = group.Where(c => !c.Removed).Aggregate(Vector3.Zero, (a, c) => a + patch.WorldOf(c)) / Math.Max(1, group.Count(c => !c.Removed));
        if (group.Count == 0) at = mid;
        Vector3 to = new Vector3(at.X - feet.X, 0f, at.Z - feet.Z).Normalized();
        Vector3 eye = feet + Vector3.Up * 2.3f + new Vector3(to.Z, 0f, -to.X) * 2f;
        _game.Visor.Visible = false;
        HideCastaway(true);
        Look(eye, at + Vector3.Up * 0.9f);
        foreach (int x in _drive.Wait(20)) yield return x;
        GD.Print($"(the group {at.DistanceTo(eye):F0} m off; {string.Join("; ", group.Take(4).Select(c => $"{c.Species.CommonName}: {c.State}{(c.Species.Figure is { } fg ? $", {fg.Dress}, {string.Join(" ", fg.Wear)}{(fg.Prop != "" ? ", " + fg.Prop : "")}" : "")}"))})");
        foreach (int x in Shot(_args.GetValueOrDefault("out", "own.png"))) yield return x;
        patch.Sim.Targets.Add(patch.Player);
    }

    /// <summary>
    /// --view=own-inside (with --force-family and --force-occupant, a place of a family near the
    /// landing, held by whom it says): inside the place by day, a room with its holders in it. Frame: --out.
    /// </summary>
    IEnumerable<int> OwnInside()
    {
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        foreach (int x in _drive.UntilSettled()) yield return x;
        string family = PlanetSurface.Poi.PoiSiting.ForceFamily;
        Vector3 from = _game.Survivor.WorldFeet;
        // The forced place if it was seated; else the nearest abandoned place with rooms below ground, held as asked (STAGING, printed).
        PoiSite site = _game.Zone.Planet.Pois.Where(p => p.Instance != null && (family == null || p.Family.Id == family) && p != _game.Bunker.Poi)
            .OrderBy(p => _game.Places.DistanceTo(p, from)).FirstOrDefault();
        if (site == null)
        {
            site = _game.Zone.Planet.Pois.Where(p => p.Instance != null && p != _game.Bunker.Poi && p.Instance.State is PoiState.Abandoned or PoiState.Ruined && !_game.Places.MemoryOf(p).Prepared
                    && p.Instance.Layout.Modules.Any(m => m.Storey < 0)).OrderBy(p => _game.Places.DistanceTo(p, from)).First();
            if (PlanetSurface.Poi.PoiSiting.ForceOccupancy is { } occ) site.Instance.Occupancy = occ;
            GD.Print($"(STAGING: no {family} was seated; {site.Name} [{site.Family.Id}] held by {site.Instance.Occupancy} instead)");
        }
        GD.Print($"({site.Name} [{site.Family.Id}], held by {site.Instance.Occupancy}, {site.Instance.State})");
        Vector3 gate = Gate(site);
        PoiGenerator.Gen.Int3 f = PoiGenerator.Gen.Dirs.Vec(site.Instance.Layout.EntranceFacing);
        foreach (int x in GoTo(site, Ground(gate + new Vector3(f.X, 0f, f.Z) * 12f) + Vector3.Up * 0.2f)) yield return x;
        foreach (int x in Daylight()) yield return x;
        Place p = _game.Places.PlaceOf(site);
        for (int i = 0; i < 900 && _game.Zone.Wildlife.Sites.All(s => s.Sim != p.Memory.Fauna?.Sim); i++) yield return 0;
        DeepSite deep = _game.Zone.Wildlife.Sites.FirstOrDefault(s => s.Sim == p.Memory.Fauna?.Sim);
        if (deep == null) throw new InvalidOperationException("the place's creatures never stood");
        WorldLife life = _game.Zone.Wildlife.Life;
        var held = deep.Sim.Creatures.Where(c => !c.Dead && !c.Removed && c.Species.Role != DeepRole.Subsister).ToList();
        GD.Print($"(inside: {string.Join(", ", held.GroupBy(c => c.Species.CommonName).Select(g => $"{g.Count()} {g.Key}"))})");
        // The busiest room: the holder with most of its own round it, the world's own first.
        Creature target = held.OrderByDescending(c => life.Signature.Is(c.Species) || c.Species.Hosts?.Any(life.Signature.Is) == true ? 1 : 0)
            .ThenByDescending(c => held.Count(o => o.Position.DistanceTo(c.Position) < 6f)).FirstOrDefault()
            ?? throw new InvalidOperationException("nothing holds the place");
        _game.Torch.Visible = true;
        _game.Visor.Visible = false;
        foreach (int x in DenView(p, deep, target.Position + Vector3.Up * 0.9f, (float)Num("away", 4.5), 1.6f)) yield return x;
        foreach (int x in Shot(_args.GetValueOrDefault("out", "own-inside.png"))) yield return x;
    }
}
