using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Planets.Gen;
using PlanetSurface.Infra;
using PlanetSurface.Poi;
using PlanetSurface.World;
using PoiGenerator.Gen;
using Archetype = Planets.Gen.Archetype;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace PlanetSurface;

/// <summary>Cities on fresh worlds, and the deep, occupied places a start can reach.</summary>
public partial class SurfaceTest
{
    /// <summary>
    /// A city planned on fresh lived-in worlds: its districts and towers, its streets with no elbow, every lot
    /// fronting its street with its gate on its lot line and its crossover on the street's centre line, every
    /// lot on its own pad, nothing built on a road that isn't the city's; and only worlds whose layers call
    /// for a city get one.
    /// </summary>
    async Task CityChecks()
    {
        var rand = new Random();
        bool was = CityPlan.Force;
        int cities = 0, lots = 0, elbows = 0, streets = 0, noStreet = 0, offLine = 0, offStreet = 0, offPad = 0, onRoad = 0, worlds = 0, wrongWorld = 0;
        int tallest = 0, towers = 0, kinds = 0, shore = 0, wetStreets = 0, walls = 0, wrongWalls = 0;
        double slowest = 0, worstTurn = 0;
        string first = "", firstWorld = "";
        void Note(string s) { if (first.Length == 0) first = $" (first: {s})"; }
        try
        {
            CityPlan.Force = true;
            for (int k = 0; k < 6 && cities < 2; k++)
            {
                var spec = PlanetSpec.Roll((ulong)rand.NextInt64(), Archetype.Temperate, 0);
                PlanetWorld w = await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
                CityPlan city = w.Infra.City;
                if (city == null || city.Sites.Count == 0) { GD.Print($"      no city on {spec}: {city?.Why ?? "not planned (no plan or never lived in)"}"); continue; }
                cities++;
                slowest = Math.Max(slowest, city.PlanMs);
                double width = w.Width, height = w.Height;
                var built = city.Lots.Where(l => l.Site != null).ToList();
                lots += built.Count;
                tallest = Math.Max(tallest, built.Where(l => l.Kind == "tower").Select(l => l.Lot.Storeys).DefaultIfEmpty(0).Max());
                towers += built.Count(l => l.Kind == "tower" && !l.Lot.Toppled && l.Lot.Storeys >= 8);
                kinds = Math.Max(kinds, built.Select(l => l.Kind).Distinct().Count());
                GD.Print($"      {spec}: city of {built.Count} buildings ({string.Join(", ", built.GroupBy(l => l.Kind).Select(g => $"{g.Key} {g.Count()}"))}), " +
                         $"{city.Streets.Count} streets, bank {(city.Bank != null ? "yes" : "no")}, {city.Bridges.Count} bridges, fall {city.Fall:F2}, {city.PlanMs:F0} ms");
                // No elbow: over any 8 m a street turns no more than a town corner's curve does.
                foreach (InfraRoute r in city.Streets.Append(city.Bank).Where(r => r != null))
                {
                    streets++;
                    for (int i = 3; i + 4 < r.Count; i++)
                    {
                        double a0 = Math.Atan2(r.Z[i] - r.Z[i - 2], r.X[i] - r.X[i - 2]), a1 = Math.Atan2(r.Z[i + 2] - r.Z[i], r.X[i + 2] - r.X[i]);
                        double turn = Math.Abs(Math.IEEERemainder(a1 - a0, Math.PI * 2)) * 180 / Math.PI;
                        worstTurn = Math.Max(worstTurn, turn);
                        if (turn <= 60) continue;
                        elbows++;
                        Note($"a street turns {turn:F0} degrees at {r.X[i]:F0},{r.Z[i]:F0}");
                        i += 4;
                    }
                }
                // Nothing at the water's edge but a waterfront's wall: no lot over the water or on the shore beside it,
                // no street over it; each waterfront's wall faces the water with the land dropping away in front of it.
                bool Wet(double x, double z) => w.Source.Sampler.Sample(Mathx.WrapX(x, width), Mathx.Wrap(z, height), 2) is var g && (g.InRiver || g.Height < 2.5f);
                foreach (CityLotPlan lot in built)
                {
                    bool wet = false;
                    for (double x = lot.X0 - 4; x <= lot.X1 + 4 && !wet; x += 4)
                        for (double z = lot.Z0 - 4; z <= lot.Z1 + 4 && !wet; z += 4) wet = Wet(x, z);
                    if (wet) { shore++; Note($"a {lot.Kind} stands at the water's edge"); }
                }
                foreach (InfraRoute r in city.Streets)
                    for (int i = 1; i + 1 < r.Count; i++)
                    {
                        double dx = r.X[i + 1] - r.X[i - 1], dz = r.Z[i + 1] - r.Z[i - 1], l = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz)), px = -dz / l * (r.HalfWidth + 1), pz = dx / l * (r.HalfWidth + 1);
                        if (r.Span[Math.Min(i, r.Span.Length - 1)] == GalHack.Terrain.RoadSpan.Ground && (Wet(r.X[i], r.Z[i]) || Wet(r.X[i] + px, r.Z[i] + pz) || Wet(r.X[i] - px, r.Z[i] - pz)))
                        { wetStreets++; Note($"a street runs at the water's edge at {r.X[i]:F0},{r.Z[i]:F0}"); break; }
                    }
                foreach (CityPlan.Seawall s in city.Waterfront)
                {
                    walls++;
                    InfraRoute r = s.Road;
                    int facing = 0, dropping = 0, points = 0;
                    for (int i = 2; i + 2 < r.Count; i += 3)
                    {
                        double dx = r.X[i + 1] - r.X[i - 1], dz = r.Z[i + 1] - r.Z[i - 1], l = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
                        double ox = -dz / l * r.WallSide, oz = dx / l * r.WallSide, reach = r.HalfWidth + GalHack.Terrain.TerrainStamp.WallStep;
                        points++;
                        if (Enumerable.Range(1, 12).Any(t => Wet(r.X[i] + ox * (reach + t * 3), r.Z[i] + oz * (reach + t * 3)))) facing++;
                        if (w.GroundHeight(Mathx.WrapX(r.X[i] + ox * (reach + 2.5), width), Mathx.Wrap(r.Z[i] + oz * (reach + 2.5), height)) < r.Y[i] - 0.3f) dropping++;
                    }
                    if (points > 0 && facing < points * 0.7) { wrongWalls++; Note($"a waterfront's wall faces the water at only {facing} of {points} points"); }
                    if (points > 0 && dropping < points * 0.7) { wrongWalls++; Note($"the land drops in front of a waterfront's wall at only {dropping} of {points} points"); }
                }
                // The other roads: the network's own, which nothing of the city's may stand on.
                var others = w.Infra.Routes.Where(r => !city.Streets.Contains(r)).ToList();
                foreach (CityLotPlan lot in built)
                {
                    bool alongX = lot.Front is Dir.North or Dir.South;
                    double line = lot.Front switch { Dir.North => lot.Z0, Dir.South => lot.Z1, Dir.West => lot.X0, _ => lot.X1 };
                    if (lot.Street == null) { noStreet++; Note($"a {lot.Kind} has no street"); continue; }
                    // The gate on the front lot line, inside the frontage.
                    double gAcross = alongX ? Mathx.WrapDelta(lot.Gate.Z - line, height) : Mathx.WrapDelta(lot.Gate.X - line, width);
                    double gAlong = alongX ? Mathx.WrapDelta(lot.Gate.X - lot.X0, width) : Mathx.WrapDelta(lot.Gate.Z - lot.Z0, height);
                    if (Math.Abs(gAcross) > 0.01 || gAlong < 0 || gAlong > (alongX ? lot.X1 - lot.X0 : lot.Z1 - lot.Z0)) { offLine++; Note($"a {lot.Kind}'s gate is {gAcross:F1} m off its lot line"); }
                    // The crossover on its street's centre line.
                    double near = double.MaxValue;
                    InfraRoute s = lot.Street;
                    for (int i = 0; i + 1 < s.Count; i++)
                    {
                        double ax = Mathx.WrapDelta(s.X[i] - lot.Crossover.X, width), az = Mathx.WrapDelta(s.Z[i] - lot.Crossover.Z, height);
                        double ex = s.X[i + 1] - s.X[i], ez = s.Z[i + 1] - s.Z[i], len2 = ex * ex + ez * ez;
                        double t = len2 < 1e-9 ? 0 : Math.Clamp(-(ax * ex + az * ez) / len2, 0, 1);
                        near = Math.Min(near, Math.Sqrt(Math.Pow(ax + ex * t, 2) + Math.Pow(az + ez * t, 2)));
                    }
                    if (near > 1.0) { offStreet++; Note($"a {lot.Kind}'s crossover is {near:F1} m off its street's centre line"); }
                    // On its pad: the ground at the lot's middle is the lot's.
                    float g = w.GroundHeight(Mathx.WrapX((lot.X0 + lot.X1) / 2, width), Mathx.Wrap((lot.Z0 + lot.Z1) / 2, height));
                    if (Math.Abs(g - lot.Pad) > 1.0f) { offPad++; Note($"a {lot.Kind}'s ground is {g - lot.Pad:F1} m off its pad"); }
                    // Nothing on another road: no point of one inside the lot, short of its edges.
                    foreach (InfraRoute r in others)
                        for (int i = 0; i < r.Count; i++)
                        {
                            double dx = Mathx.WrapDelta(r.X[i] - (lot.X0 + lot.X1) / 2, width), dz = Mathx.WrapDelta(r.Z[i] - (lot.Z0 + lot.Z1) / 2, height);
                            if (Math.Abs(dx) < (lot.X1 - lot.X0) / 2 - 2 && Math.Abs(dz) < (lot.Z1 - lot.Z0) / 2 - 2) { onRoad++; Note($"a {lot.Kind} stands on a {r.Kind}"); goto nextLot; }
                        }
                    nextLot:;
                }
            }
            // Only a world that was crowded, or whose layers call for one, gets a city.
            CityPlan.Force = false;
            for (int k = 0; k < 3; k++)
            {
                var spec = PlanetSpec.Roll((ulong)rand.NextInt64(), Archetype.Temperate, 0);
                PlanetWorld w = await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
                worlds++;
                WorldRoll roll = w.Infra.RegionPlan?.Roll;
                bool wanted = roll != null && w.Profile is { Inhabited: true } && CityPlan.Wanted(roll, w.Profile), has = w.Infra.City != null;
                if (wanted != has) { wrongWorld++; if (firstWorld.Length == 0) firstWorld = $" (first: {roll}: wanted {wanted}, has {has})"; }
            }
        }
        finally { CityPlan.Force = was; }
        Check(cities >= 1, $"a city plans on fresh lived-in worlds ({cities})");
        Check(towers >= 4 && tallest >= 12 && kinds >= 5, $"a city has a downtown of towers and districts of other kinds: {towers} towers of 8 storeys and more, the tallest {tallest}, {kinds} kinds of building");
        Check(elbows == 0, $"no city street has an elbow: {streets} streets, none turning more than 60 degrees over 8 m (sharpest {worstTurn:F0}){first}");
        Check(noStreet == 0 && offLine == 0 && offStreet == 0, $"every lot fronts a street, its gate on its lot line and its crossover on the street's centre line: {lots} lots, {noStreet} with no street, {offLine} gates off the line, {offStreet} crossovers off the street{first}");
        Check(offPad == 0, $"every lot stands on its own pad ({offPad} of {lots} off it){first}");
        Check(onRoad == 0, $"nothing a city builds stands on a road not its own ({onRoad} of {lots}){first}");
        Check(shore == 0 && wetStreets == 0, $"no city building or street sits on the shore or over the water: {lots} lots, {shore} at the water's edge, {wetStreets} streets running along it{first}");
        Check(walls >= 1 && wrongWalls == 0, $"where a city meets the water it meets it with a wall: {walls} waterfronts, each facing the water with the land dropping in front ({wrongWalls} wrong){first}");
        Check(slowest < 4000, $"a city plans in under 4 s (slowest {slowest:F0} ms)");
        Check(wrongWorld == 0, $"only worlds whose layers call for a city get one: {worlds} fresh worlds, {wrongWorld} wrong{firstWorld}");
    }

    /// <summary>
    /// A dungeon is a deep place: rooms below ground, enough of them to be a place you go into, and someone or
    /// something living there. Ordinary start worlds still roll them, and one lies within reach of the start.
    /// </summary>
    static bool Dungeon(PoiSite p) => StartTown.Deep(p.Layout) && StartTown.Lived(p.Instance);

    /// <summary>The distance from the start (the start town's edge, or the landing) to the nearest dungeon, metres.</summary>
    static double ToDungeon(PlanetWorld w, out int count)
    {
        double sx = w.StartTown?.EdgeX ?? w.Landing.X, sz = w.StartTown?.EdgeZ ?? w.Landing.Y;
        // (The start town's own dungeon counts even where it is only enclosed, not deep: the nearest big place in reach, given people.)
        var deep = w.Pois.Where(p => Dungeon(p) || p == w.StartTown?.Dungeon && StartTown.Lived(p.Instance)).ToList();
        count = deep.Count;
        return deep.Select(p => Math.Sqrt(Math.Pow(Mathx.WrapDelta(p.Center.X - sx, w.Width), 2) + Math.Pow(Mathx.WrapDelta(p.Center.Z - sz, w.Height), 2))).DefaultIfEmpty(double.MaxValue).Min();
    }

    async Task DungeonChecks()
    {
        var rand = new Random();
        bool was = PoiSiting.StartWorld;
        int worlds = 0, reached = 0, total = 0, none = 0;
        var nearest = new List<double>();
        try
        {
            PoiSiting.StartWorld = true;
            for (int k = 0; k < 4; k++)
            {
                var spec = PlanetSpec.Roll((ulong)rand.NextInt64(), Archetype.Temperate, 0);
                PlanetWorld w = await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
                worlds++;
                double d = ToDungeon(w, out int count);
                total += count;
                if (count == 0) none++;
                if (d <= DungeonReach) reached++;
                nearest.Add(d);
                var deep = w.Pois.Where(Dungeon).Take(4).Select(p => $"{p.Family.Id} ({p.Instance.State}, {p.Instance.Occupancy?.Main.ToString() ?? "-"})");
                double deepest = w.Pois.Where(p => StartTown.Deep(p.Layout))
                    .Select(p => Math.Sqrt(Math.Pow(Mathx.WrapDelta(p.Center.X - (w.StartTown?.EdgeX ?? w.Landing.X), w.Width), 2) + Math.Pow(Mathx.WrapDelta(p.Center.Z - (w.StartTown?.EdgeZ ?? w.Landing.Y), w.Height), 2))).DefaultIfEmpty(-1).Min();
                GD.Print($"      {spec}: {count} dungeons of {w.Pois.Count} places, nearest {(d < double.MaxValue ? $"{d:F0} m" : "none")} from the start (deep place nearest {deepest:F0} m; " +
                         $"{(w.StartTown == null ? "no start town" : $"start town's dungeon {w.StartTown.Dungeon?.Family.Id ?? "none"}, landmark {w.StartTown.Landmark.Family.Id}")}); {string.Join(", ", deep)}");
            }
        }
        finally { PoiSiting.StartWorld = was; }
        Check(none == 0, $"every fresh start world rolls dungeons: {total} on {worlds} worlds ({(worlds == 0 ? 0 : total / (double)worlds):F1} a world), {none} with none");
        Check(reached == worlds, $"a dungeon lies within {DungeonReach / 1000:F1} km of every start: {reached} of {worlds} (nearest {string.Join(", ", nearest.Select(d => d < double.MaxValue ? $"{d:F0} m" : "none"))})");
    }

    /// <summary>How far from the start the nearest dungeon may lie, metres.</summary>
    const double DungeonReach = 2500;
}
