using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Places;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The place surfaces proof, frame by frame into --dir, each name led by --tag (before, after), in late
/// morning light on the world as it rolled: a street of houses, a town's open middle, an office inside,
/// a ruined concrete wall close up, an industrial site, an alien place (the --force family, set down near
/// the start), a building at 20 m, and the town or city from the air. A city is planned on any lived-in
/// world for it. --only=street,square,... takes some of them.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> PlaceSurfView()
    {
        string dir = _args.GetValueOrDefault("dir", "."), tag = _args.GetValueOrDefault("tag", "shot");
        System.IO.Directory.CreateDirectory(dir);
        var only = _args.TryGetValue("only", out string o) ? new HashSet<string>(o.Split(',')) : null;
        bool Want(string s) => only == null || only.Contains(s);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        _game.Visor.Visible = false;
        var planet = _game.Zone.Planet;
        Vector3 start = _game.Survivor.WorldFeet;
        GD.Print($"placesurf: families here: {string.Join(" ", planet.Pois.Select(s => s.Family.Id).Distinct().OrderBy(s => s))}");
        string Name(string what) => $"{dir}/{tag}-{what}.png";
        float Away(PoiSite s) => _game.Places.DistanceTo(s, start);

        if (Want("street")) foreach (int x in SurfStreet(Name("street"))) yield return x;

        // A town: the city's middle where there is one (its buildings round a crossing), else a settlement where people live,
        // else the nearest built-up place; its open middle, a wall at 20 m, and from the air.
        if (planet.Infra?.City is { Sites.Count: > 0 } city && Want("square"))
        {
            Vector3 middle = planet.NearestCopy(new Vector3((float)city.CenterX, 0f, (float)city.CenterZ), start);
            PoiSite first = planet.Pois.Where(s => s.Siting == "city").OrderBy(s => _game.Places.DistanceTo(s, middle)).First();
            Vector3 feet = middle with { Y = _game.Zone.GroundAt(middle.X, middle.Z).Y + 0.2f };
            foreach (int x in GoTo(first, feet)) yield return x;
            foreach (int x in _drive.Wait(240)) yield return x;
            // Out into the open: the clearest of a few spots round the middle, looking at the nearest buildings.
            var near = planet.Pois.Where(s => s.Siting == "city").OrderBy(s => _game.Places.DistanceTo(s, middle)).Take(4).ToList();
            Vector3 aim = near.Aggregate(Vector3.Zero, (a, s) => a + planet.NearestCopy(new Vector3((float)s.Center.X, 0f, (float)s.Center.Z), middle)) / near.Count;
            Vector3 eye = middle;
            float best = -1f;
            var space = _game.Zone.GetWorld3D().DirectSpaceState;
            for (int a = 0; a < 12; a++)
                for (float r = 0f; r <= 24f; r += 8f)
                {
                    Vector3 c = middle + new Vector3(MathF.Cos(a * MathF.Tau / 12f), 0f, MathF.Sin(a * MathF.Tau / 12f)) * r;
                    c.Y = _game.Zone.GroundAt(c.X, c.Z).Y + 1.7f;
                    float open = 0f;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 d = new(MathF.Cos(k * MathF.Tau / 8f), 0f, MathF.Sin(k * MathF.Tau / 8f));
                        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(c, c + d * 30f, GalHack.Physics.Layers.World));
                        open += hit.Count == 0 ? 30f : ((Vector3)hit["position"]).DistanceTo(c);
                    }
                    if (open > best) { best = open; eye = c; }
                }
            aim.Y = eye.Y + 4f;
            GD.Print($"placesurf: square in the city's middle, {near.Count} buildings round it");
            foreach (int x in Frame(eye, aim, Name("square"))) yield return x;
            only?.Remove("square");
        }
        PoiSite town = planet.Pois.Where(s => s.Siting == "city").OrderBy(Away).FirstOrDefault()
            ?? planet.Pois.Where(s => s.Instance?.Townsfolk != null).OrderBy(Away).FirstOrDefault()
            ?? planet.Pois.OrderByDescending(s => s.Instance.Layout.Modules.Count).FirstOrDefault();
        if (town != null && (Want("square") || Want("20m") || Want("air")))
        {
            GD.Print($"placesurf: town {town.Family.Id} '{town.Name}' {town.Instance.State}, {Away(town):F0} m");
            foreach (int x in GoTo(town, Gate(town) + Vector3.Up * 0.1f)) yield return x;
            Place p = _game.Places.PlaceOf(town);
            if (p != null)
            {
                if (Want("square") && planet.Infra?.City is not { Sites.Count: > 0 }) foreach (int x in SurfSquare(p, Name("square"))) yield return x;
                if (Want("20m")) foreach (int x in SurfFacade(p, 20f, Name("20m"))) yield return x;
                if (Want("air")) foreach (int x in SurfAir(p, Name("air"))) yield return x;
            }
        }

        if (Want("office"))
        {
            string[] offices = { "office", "archive", "security", "control", "command", "lobby" };
            bool Lit(PoiSite s, PlacedModule m) => s.Instance.Lights.Any(l => l.Module == m.Index);
            PoiSite site = planet.Pois.Where(s => s.Instance.State != PoiState.Ruined && s.Instance.Layout.Modules.Any(m => offices.Contains(m.Purpose) && m.Min.Y >= 0 && Lit(s, m))).OrderBy(Away).FirstOrDefault()
                ?? planet.Pois.Where(s => s.Instance.Layout.Modules.Any(m => offices.Contains(m.Purpose) && m.Min.Y >= 0)).OrderBy(Away).FirstOrDefault();
            if (site == null) GD.Print("placesurf: no office on this world");
            else
            {
                GD.Print($"placesurf: office in {site.Family.Id} '{site.Name}' {site.Instance.State}");
                foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
                Place p = _game.Places.PlaceOf(site);
                PlacedModule room = site.Instance.Layout.Modules.Where(m => offices.Contains(m.Purpose) && m.Min.Y >= 0).OrderByDescending(m => Lit(site, m)).ThenBy(m => Array.IndexOf(offices, m.Purpose)).ThenByDescending(m => m.Cells.Count()).First();
                if (p != null) foreach (int x in SurfInside(p, room, Name("office"))) yield return x;
            }
        }

        if (Want("ruin"))
        {
            // A ruined concrete building: its outer wall close up, where the years have had it longest.
            PoiSite site = planet.Pois.Where(s => s.Instance.State == PoiState.Ruined && s.Family.Id is "city_tower" or "city_midrise" or "city_works" or "city_station" && s.Instance.Blocks.Blocks().Take(20000).Count(b => b.Block.Role == Role.Wall && b.Block.Material == PoiGenerator.Gen.Material.Concrete) > 60).OrderBy(Away).FirstOrDefault()
                ?? planet.Pois.Where(s => s.Instance.State == PoiState.Ruined && s.Instance.Blocks.Blocks().Take(20000).Count(b => b.Block.Role == Role.Wall && b.Block.Material == PoiGenerator.Gen.Material.Concrete) > 60)
                .OrderBy(Away).FirstOrDefault()
                ?? planet.Pois.Where(s => s.Instance.State != PoiState.Active && s.Instance.Kit.Id is "concrete" or "urban" or "military").OrderBy(Away).FirstOrDefault();
            if (site == null) GD.Print("placesurf: no ruined concrete building on this world");
            else
            {
                GD.Print($"placesurf: ruin {site.Family.Id} '{site.Name}' {site.Instance.State}, kit {site.Instance.Kit.Id}");
                foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
                Place p = _game.Places.PlaceOf(site);
                if (p != null) foreach (int x in SurfFacade(p, 2.6f, Name("ruin"))) yield return x;
            }
        }

        if (Want("industrial"))
        {
            PoiSite site = planet.Pois.Where(s => s.Instance.Kit.Id == "industrial" || s.Family.Tags.Contains("industrial") || s.Family.Id.Contains("factory") || s.Family.Id is "steelworks" or "refinery" or "coal_plant" or "rail_yard" or "docks" or "sewage_works" or "water_works" or "supply_depot")
                .OrderBy(Away).FirstOrDefault();
            if (site == null) GD.Print("placesurf: no industrial site on this world");
            else
            {
                GD.Print($"placesurf: industrial {site.Family.Id} '{site.Name}' {site.Instance.State}");
                foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
                Place p = _game.Places.PlaceOf(site);
                if (p != null) foreach (int x in SurfFacade(p, 32f, Name("industrial"))) yield return x;
            }
        }

        if (Want("alien"))
        {
            string force = _args.GetValueOrDefault("force", "alien_cathedral");
            PoiSite site = planet.Pois.Where(s => s.Family.Id == force).OrderBy(Away).FirstOrDefault()
                ?? planet.Pois.Where(s => s.Instance.Kit.Id.StartsWith("alien") || s.Instance.Kit.Id == "precursor" || s.Instance.Kit.Faction is Faction.Vharn or Faction.Kessari).OrderBy(Away).FirstOrDefault();
            if (site == null) GD.Print("placesurf: no alien place on this world");
            else
            {
                GD.Print($"placesurf: alien {site.Family.Id} '{site.Name}' {site.Instance.State}, kit {site.Instance.Kit.Id}");
                foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
                Place p = _game.Places.PlaceOf(site);
                if (p != null)
                {
                    foreach (int x in SurfFacade(p, 18f, Name("alien"))) yield return x;
                    foreach (int x in SurfFacade(p, 3f, Name("alien-close"))) yield return x;
                }
            }
        }
        _game.FixedCamera = null;
        _game.Visor.Visible = true;
    }

    /// <summary>The middle of a street of houses along a road, at eye height, looking down it.</summary>
    IEnumerable<int> SurfStreet(string file)
    {
        var planet = _game.Zone.Planet;
        Vector3 from = _game.Survivor.WorldFeet;
        float Away(PlanetSurface.Infra.FlavourRuin r) => planet.NearestCopy(new Vector3((float)r.Center.X, from.Y, (float)r.Center.Z), from).DistanceTo(from);
        var ruins = planet.Infra.Ruins.Where(r => r.Route != null && r.Blast < 0.05f && r.Family.Tags.Contains("house")
            && planet.Infra.Ruins.Count(q => q.Route == r.Route && q.Family.Tags.Contains("house")) >= 4).OrderBy(Away).ToList();
        if (ruins.Count == 0) { GD.Print("placesurf: no street of houses on this world"); yield break; }
        PlanetSurface.Infra.FlavourRuin ruin = ruins[0];
        PlanetSurface.Infra.InfraRoute road = ruin.Route;
        int near = Enumerable.Range(0, road.Count).OrderBy(i => Mathf.Abs((float)Planets.Gen.Mathx.WrapDelta(road.X[i] - ruin.Center.X, planet.Width)) + Mathf.Abs((float)Planets.Gen.Mathx.WrapDelta(road.Z[i] - ruin.Center.Z, planet.Height))).First();
        int at = Math.Clamp(near - (int)(24 / PlanetSurface.Infra.InfraNetwork.Spacing), 0, road.Count - 1);
        Vector3 stand = planet.NearestCopy(new Vector3((float)road.X[at], road.Y[at], (float)road.Z[at]), from);
        stand.Y = _game.Zone.GroundAt(stand.X, stand.Z).Y + 0.2f;
        _game.Survivor.PlaceOutside(stand.ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(600)) yield return x;
        _game.Zone.Sky.TimeOfDay = LocalDay();
        stand = _game.Survivor.WorldFeet;
        int ahead = Math.Clamp(near + 6, 0, road.Count - 1);
        Vector3 look = planet.NearestCopy(new Vector3((float)road.X[ahead], road.Y[ahead] + 2.5f, (float)road.Z[ahead]), stand);
        GD.Print($"placesurf: street of {ruin.Layout.Name} ({ruin.State}), {ruins.Count} to choose from");
        foreach (int x in _drive.LookAt(look, 20)) yield return x;
        foreach (int x in _drive.Wait(60)) yield return x;
        foreach (int x in Shot(file)) yield return x;
    }

    /// <summary>A town's open middle: the biggest yard or street in it, from its edge at eye height, looking across.</summary>
    IEnumerable<int> SurfSquare(Place p, string file)
    {
        var mods = p.Inst.Layout.Modules.Where(m => m.Kind == ModuleKind.Outdoor && m.Min.Y >= -1).OrderByDescending(m => m.Cells.Count(c => c.Y == m.Min.Y)).ToList();
        Vector3 eye, look;
        if (mods.Count > 0)
        {
            PlacedModule m = mods[0];
            var cells = m.Cells.Where(c => c.Y == m.Min.Y).ToList();
            Vector3 mid = new((float)cells.Average(c => c.X), m.Min.Y, (float)cells.Average(c => c.Z));
            Int3 edge = cells.OrderByDescending(c => (c.X - mid.X) * (c.X - mid.X) + (c.Z - mid.Z) * (c.Z - mid.Z)).Skip(cells.Count / 10).First();
            eye = p.WorldOf(edge, 1.7f);
            look = p.ToWorld(mid + new Vector3(0.5f, 2.5f, 0.5f));
        }
        else
        {
            Vector3 gate = Gate(p.Site);
            Int3 f = p.Site.Instance.Layout.EntranceFacing.Vec();
            eye = gate + new Vector3(f.X, 0f, f.Z) * 14f + Vector3.Up * 1.7f;
            look = gate + Vector3.Up * 3f;
        }
        eye.Y = Math.Max(eye.Y, _game.Zone.GroundAt(eye.X, eye.Z).Y + 1.7f);
        foreach (int x in Frame(eye, look, file)) yield return x;
    }

    /// <summary>A place's most built-up wall from a distance, at eye height, the sunny side first (as the facade view frames it).</summary>
    IEnumerable<int> SurfFacade(Place p, float dist, string file)
    {
        var cells = p.Inst.Blocks.Blocks().Where(b => b.Block.Role is Role.Wall or Role.Window && b.Cell.Y >= 0).Select(b => b.Cell).ToList();
        if (cells.Count == 0) { GD.Print($"placesurf: {p.Site.Family.Id} has nothing above ground"); yield break; }
        var heart = cells.GroupBy(c => (c.X >> 4, c.Z >> 4)).OrderByDescending(g => g.Count()).First().ToList();
        Int3 mid = new((int)heart.Average(c => c.X), (int)heart.Average(c => c.Y), (int)heart.Average(c => c.Z));
        Vector3 target = p.WorldOf(mid);
        Vector3 sun = _game.Zone.Sky.ToSun;
        Int3 f = p.Site.Instance.Layout.EntranceFacing.Vec();
        float Bearing(int a) => MathF.Atan2(f.Z, f.X) + a * MathF.Tau / 8f;
        Vector3 way = Vector3.Forward, wall = target;
        int most = -1;
        foreach (int a in Enumerable.Range(0, 8).OrderByDescending(a => new Vector3(MathF.Cos(Bearing(a)), 0f, MathF.Sin(Bearing(a))).Dot(sun) + (a == 0 ? 0.3f : 0f)))
        {
            if (most >= 9) break;
            var (w, at) = WallFrom(target, Bearing(a));
            _game.Survivor.PlaceOutside((StandOff(at, w, dist) + Vector3.Down * 1.5f).ToNumerics());
            foreach (int x in _drive.Wait(45)) yield return x;
            int clear = Sight(StandOff(at, w, dist), at, w);
            if (clear > most) { most = clear; way = w; wall = at; }
        }
        Vector3 eye = StandOff(wall, way, dist);
        float rise = dist < 5f ? -0.25f : 1.2f + dist * 0.04f;
        foreach (int x in Frame(eye, wall with { Y = eye.Y + rise }, file)) yield return x;
    }

    /// <summary>A place from the air: up over its edge, looking down across it.</summary>
    IEnumerable<int> SurfAir(Place p, string file)
    {
        var lo = p.Site.Footprint.Min; var hi = p.Site.Footprint.Max;
        Vector3 mid = _game.Zone.Planet.NearestCopy(new Vector3((lo.X + hi.X + 1) * 0.5f, 0f, (lo.Z + hi.Z + 1) * 0.5f), _game.Survivor.WorldFeet);
        mid.Y = _game.Zone.GroundAt(mid.X, mid.Z).Y;
        float r = Math.Max(30f, Math.Max(hi.X - lo.X, hi.Z - lo.Z) * 0.6f);
        Vector3 sun = _game.Zone.Sky.ToSun with { Y = 0f };
        Vector3 back = sun.LengthSquared() > 0.01f ? sun.Normalized() : Vector3.Back;
        Vector3 eye = mid + back * r + Vector3.Up * (r * 0.9f + 20f);
        foreach (int x in Frame(eye, mid + Vector3.Up * 4f, file, settle: 240)) yield return x;
    }

    /// <summary>Inside a room: from its far corner at eye height, the lamps as they are, the torch off.</summary>
    IEnumerable<int> SurfInside(Place p, PlacedModule room, string file)
    {
        var floor = room.Cells.Where(c => c.Y == room.Min.Y && p.Inst.Blocks.Get(c).IsAir).ToList();
        if (floor.Count == 0) yield break;
        Vector3 mid = new((float)floor.Average(c => c.X), room.Min.Y, (float)floor.Average(c => c.Z));
        Int3 corner = floor.OrderByDescending(c => (c.X - mid.X) * (c.X - mid.X) + (c.Z - mid.Z) * (c.Z - mid.Z)).First();
        Vector3 eye = p.WorldOf(corner, 1.6f), look = p.ToWorld(mid + new Vector3(0.5f, 0.9f, 0.5f));
        // Seen as the player sees it, standing in the corner with the torch on.
        _game.Survivor.PlaceOutside((eye + Vector3.Down * 1.6f).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        _game.Zone.Sky.TimeOfDay = LocalDay();
        _game.Torch.Visible = true;
        foreach (int x in _drive.LookAt(look, 30)) yield return x;
        foreach (int x in _drive.Wait(60)) yield return x;
        foreach (int x in Shot(file)) yield return x;
        _game.Torch.Visible = false;
    }

    /// <summary>An outer wall close up (2.4 m): the most weathered stretch of it that looks out at the open, a little above the ground.</summary>
    IEnumerable<int> SurfClose(Place p, string file)
    {
        var g = p.Inst.Blocks;
        var occ = p.Inst.Layout.Occupied;
        (Int3 Cell, Dir Out)? best = null;
        int score = -1;
        foreach (var (c, b) in g.Blocks())
        {
            if (b.Role != Role.Wall || !BlockInfo.IsFullCube(b.Type) || c.Y < 0 || c.Y > 4) continue;
            foreach (Dir d in Dirs.Horizontal)
            {
                Int3 n = c + d.Vec();
                if (!g.Get(n).IsAir || occ[n] && !g.Get(n + Int3.Up * 3).IsAir) continue;
                // A stretch of wall: the cells beside and above it are wall too, and nothing stands in front for a few metres.
                int s = 0;
                for (int k = -2; k <= 2; k++)
                    for (int up = -1; up <= 1; up++)
                    {
                        Int3 side = new Int3(d is Dir.North or Dir.South ? k : 0, up, d is Dir.East or Dir.West ? k : 0);
                        if (BlockInfo.IsFullCube(g.Get(c + side).Type)) s++;
                    }
                for (int k = 1; k <= 4; k++) if (!g.Get(c + d.Vec() * k).IsAir) s -= 6;
                s += (int)(Hash.Cell(c, 99) % 3);
                if (s > score) { score = s; best = (c, d); }
            }
        }
        if (best is not { } pick) { GD.Print("placesurf: no outer wall to see close"); yield break; }
        Vector3 at = p.WorldOf(pick.Cell, 0.6f);
        Int3 o = pick.Out.Vec();
        Vector3 outward = new(o.X, 0f, o.Z);
        Vector3 eye = at + outward * 2.9f + Vector3.Up * 0.5f;
        foreach (int x in Frame(eye, at + outward * 0.5f + Vector3.Up * 0.2f, file)) yield return x;
    }

    /// <summary>The camera fixed at an eye looking at a point, the castaway out of the shot behind it, the ground and place in, then the shot.</summary>
    IEnumerable<int> Frame(Vector3 eye, Vector3 look, string file, int settle = 120)
    {
        Vector3 back = (eye - look) with { Y = 0f };
        back = back.LengthSquared() > 0.01f ? back.Normalized() : Vector3.Back;
        Vector3 feet = eye + back * 3f;
        feet.Y = _game.Zone.GroundAt(feet.X, feet.Z).Y + 0.2f;
        _game.Survivor.PlaceOutside(feet.ToNumerics());
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(settle)) yield return x;
        _game.Zone.Sky.TimeOfDay = LocalDay();
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(60)) yield return x;
        foreach (int x in Shot(file)) yield return x;
        _game.FixedCamera = null;
    }
}
