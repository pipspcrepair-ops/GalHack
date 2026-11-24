using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using Slice.Places;

namespace Slice.Tests;

/// <summary>
/// Where the hand scanner's core sight is proved and shot, shared by CoreScanTest and the "corescan" view: the survivor stands outside a
/// place that keeps a core, with the terrain or walls between her and it (the ray to the core is blocked), within the
/// scanner's full strength, looking at the core.
/// </summary>
public sealed class CoreScanStage
{
    public Place Place;
    public Vector3 Core;

    /// <summary>Stands outside the nearest place with a core that has a blocked view of it, and looks at the core. Place stays null if none of the first few has.</summary>
    public IEnumerable<int> Go(Game.Game game, Driver drive)
    {
        Vector3 start = game.Survivor.WorldFeet;
        var space = game.Zone.GetWorld3D().DirectSpaceState;
        var sites = game.Zone.Planet.Pois.Where(s => s.Instance.HasCore && s.Instance.State != PoiGenerator.Gen.PoiState.Ruined)
            .OrderBy(s => game.Places.DistanceTo(s, start)).Take(8).ToList();
        GD.Print($"  (corescan: {sites.Count} places with cores, of {game.Zone.Planet.Pois.Count})");
        foreach (PoiSite site in sites)
        {
            Vector3 gate = game.Zone.Planet.NearestCopy(new Vector3(site.Entrance.X + 0.5f, site.Entrance.Y, site.Entrance.Z + 0.5f), start);
            game.Survivor.PlaceOutside((gate + Vector3.Up * 2f).ToNumerics());
            for (int i = 0; i < 1800 && game.Places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;
            Place place = game.Places.PlaceOf(site);
            if (place == null) continue;
            foreach (int x in drive.UntilSettled()) yield return x;
            Vector3 core = place.WorldOf(site.Instance.CoreCell);
            var g = site.Instance.Blocks;
            Vector3 mid = place.WorldOf(g.Min) + new Vector3(g.Size.X, 0f, g.Size.Z) / 2f;
            float half = MathF.Max(g.Size.X, g.Size.Z) / 2f;
            Vector3? best = null;
            float bestScore = float.MaxValue;
            foreach (float r in new[] { 3f, 8f, 14f, 22f })
                for (int a = 0; a < 24; a++)
                {
                    Vector3 p = mid + new Vector3(MathF.Cos(a * Mathf.Tau / 24f), 0f, MathF.Sin(a * Mathf.Tau / 24f)) * (half + r);
                    float ground = game.Zone.GroundAt(p.X, p.Z).Y;
                    if (!float.IsFinite(ground)) continue;
                    p.Y = ground + 0.2f;
                    Vector3 eye = p + Vector3.Up * 1.5f;
                    float d = eye.DistanceTo(core);
                    if (d < 8f || d > 85f || MathF.Abs(core.Y - eye.Y) > 0.8f * d) continue;
                    var ray = space.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, core, GalHack.Physics.Layers.World));
                    if (ray.Count == 0 || ((Vector3)ray["position"]).DistanceTo(core) < 0.5f) continue; // a clear view is no proof
                    if (MathF.Abs(d - 45f) < bestScore) { bestScore = MathF.Abs(d - 45f); best = p; }
                }
            if (best is not Vector3 stand) { GD.Print($"  (corescan: {site.Name} {site.Family.Id}: no blocked stand; core {core}, mid {mid}, half {half}, place at {place.GlobalPosition})"); continue; }
            game.Survivor.PlaceOutside(stand.ToNumerics());
            foreach (int x in drive.UntilSettled()) yield return x;
            foreach (int x in drive.Wait(40)) yield return x;
            foreach (int x in drive.LookAt(core, 12)) yield return x;
            Place = place;
            Core = core;
            yield break;
        }
    }

    /// <summary>Seats a place that keeps a core near the start of the next world: a random family of those that do.</summary>
    public static string ForceCoreFamily()
    {
        var families = Slice.Surface.SurfaceZone.PoiCatalog.Library.Families.Where(f => f.Core == PoiGenerator.Gen.CoreRule.Owned).ToList();
        PlanetSurface.Poi.PoiSiting.ForceFamily = families[Random.Shared.Next(families.Count)].Id;
        PlanetSurface.Poi.PoiSiting.ForceState = PoiGenerator.Gen.PoiState.Active;
        return PlanetSurface.Poi.PoiSiting.ForceFamily;
    }

    /// <summary>The item in the bag put on a toolbar slot, and that slot in hand (the slot, or -1 if it would not go).</summary>
    public static IEnumerable<int> Hold(Playthrough play, string item, Action<int> slot)
    {
        var pack = play.Game.Pack;
        pack.Grid.Reconcile();
        int s = Enumerable.Range(0, Slice.Items.PlayerInventory.ToolbarSlots).FirstOrDefault(i => pack.Grid[i].Item == item, -1);
        if (s < 0)
        {
            foreach (int x in play.ToToolbar(item, Enumerable.Range(0, Slice.Items.PlayerInventory.ToolbarSlots).First(i => pack.Grid[i].Item == null))) yield return x;
            foreach (int x in play.CloseMenu()) yield return x;
            s = Enumerable.Range(0, Slice.Items.PlayerInventory.ToolbarSlots).FirstOrDefault(i => pack.Grid[i].Item == item, -1);
        }
        if (s >= 0) foreach (int x in play.Select(s)) yield return x;
        slot(s);
    }
}
