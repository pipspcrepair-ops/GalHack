using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Infra;
using PlanetSurface.World;
using PoiGenerator.Gen;
using Archetype = Planets.Gen.Archetype;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace PlanetSurface;

/// <summary>The world around the places: the profile, and the old world's infrastructure.</summary>
public partial class SurfaceTest
{
    // ---- the world profile ------------------------------------------------------------------

    async Task ProfileChecks()
    {
        // The same seed rolls the same profile; seeds differ widely.
        int same = 0, inhabited = 0, start = 0, rolls = 0;
        var before = new Dictionary<TechLevel, int>();
        var now = new Dictionary<TechLevel, int>();
        int lower = 0, higher = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var spec = PlanetSpec.Roll(seed, (Archetype)(seed % 5), (int)(seed % 2));
            WorldProfile p = PlanetWorld.ProfileFor(spec), q = PlanetWorld.ProfileFor(spec), s = PlanetWorld.ProfileFor(spec, startWorld: true);
            rolls++;
            if (p == q) same++;
            if (p.Inhabited) inhabited++;
            if (s.Inhabited && s.Now <= s.Before) start++;
            if (p.Inhabited) before[p.Before] = before.GetValueOrDefault(p.Before) + 1;
            now[p.Now] = now.GetValueOrDefault(p.Now) + 1;
            if (p.Inhabited && p.Now < p.Before) lower++;
            if (p.Inhabited && p.Now > p.Before) higher++;
        }
        Check(same == rolls, $"the same seed rolls the same world profile ({same} of {rolls})");
        Check(start == rolls, $"start worlds were always inhabited, and no faction brings newer tech to them ({start} of {rolls})");
        Check(inhabited > rolls * 0.6 && inhabited < rolls, $"most worlds were inhabited, some never were ({inhabited} of {rolls})");
        Check(before.Count == 3 && now.Count == 3 && before.Values.Min() > inhabited / 6 && lower > inhabited / 4 && higher > rolls / 20,
            $"tech levels spread (before the fall {string.Join(", ", before.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}; now {string.Join(", ", now.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}); survivors below their ruins on {lower} worlds, newer tech on older ruins on {higher}");
        await Frames(1);
    }

    // ---- the infrastructure -----------------------------------------------------------------

    /// <summary>Not part of the gate (--only=InfraScan): lists seeds' networks, to find views for screenshots.</summary>
    async Task InfraScan()
    {
        foreach (int a in new[] { 0, 1 })
        for (ulong seed = 61; seed < 69; seed++)
        {
            var spec = PlanetSpec.Roll(seed, (Archetype)a, 0);
            WorldProfile p = PlanetWorld.ProfileFor(spec);
            if (!p.Inhabited || p.Before == TechLevel.Low) { GD.Print($"INFO  {(Archetype)a} {seed}: {p}"); continue; }
            PlanetWorld w = PlanetWorld.Build(spec, Main.Catalog);
            InfraNetwork net = w.Infra;
            GD.Print($"INFO  {(Archetype)a} {seed}: {p}; {string.Join(", ", net.Routes.GroupBy(r => r.Kind).Select(g => $"{g.Count()} {g.Key} {g.Sum(r => r.Length) / 1000:F1} km"))}; " +
                     $"{net.Structures.Count(s => s.Kind == RoadSpan.Bridge)} bridges, {net.Structures.Count(s => s.Kind == RoadSpan.Tunnel)} tunnels; places {string.Join(" ", w.Pois.Select(x => x.Family.Id))}");
            await Frames(1);
        }
    }

    async Task InfraChecks()
    {
        int worlds = 0, places = 0, joined = 0, routes = 0, unrouted = 0, separated = 0, never = 0, neverRoutes = 0;
        int stretches = 0, withinGrade = 0, unwalkable = 0, bridges = 0, longViaducts = 0, lowOverWater = 0, tunnels = 0, longTunnels = 0;
        int deckSamples = 0, offDeck = 0, seamSamples = 0, seamMismatch = 0, tilted = 0;

        double planBest = double.MaxValue, planWorst = 0;
        bool same = true;
        var notes = new List<string>();
        var kinds = new Dictionary<RouteKind, double>();
        for (int a = 0; a < 6; a++)
        {
            // Five archetypes, and a modern temperate world (it has highways).
            var spec = a < 5 ? PlanetSpec.Roll((ulong)(900 + a * 11), (Archetype)a, 0) : PlanetSpec.Roll(61, Archetype.Temperate, 0);
            PlanetWorld w = PlanetWorld.Build(spec, Main.Catalog);
            InfraNetwork net = w.Infra;
            worlds++;
            planBest = Math.Min(planBest, net.PlanMs);
            planWorst = Math.Max(planWorst, net.PlanMs);
            if (!w.Profile.Inhabited)
            {
                never++;
                neverRoutes += net.Routes.Count;
                continue;
            }
            routes += net.Routes.Count;
            unrouted += net.Unrouted;
            separated += net.SeaSeparated;
            foreach (InfraRoute r in net.Routes) kinds[r.Kind] = kinds.GetValueOrDefault(r.Kind) + r.Length / 1000;

            // Every place the network serves is reached at one of its gates: a route ends on the gate's cell.
            bool Near(double x, double z, PoiSite p) => p.Gates.Any(g =>
                Math.Abs(Mathx.WrapDelta(g.Cell.X + 0.5 - x, w.Width)) < 1.0 && Math.Abs(Mathx.WrapDelta(g.Cell.Z + 0.5 - z, w.Height)) < 1.0);
            foreach (PoiSite p in net.Places)
            {
                if (net.Isolated.Contains(p)) continue;   // the sea cuts it off: no route was asked of a world that never bridged it
                places++;
                bool at = net.Routes.Any(r => (r.From == p && Near(r.X[0], r.Z[0], p)) || (r.To == p && Near(r.X[^1], r.Z[^1], p)));
                if (at) joined++;
                else if (notes.Count < 4) notes.Add($"{p.Name} on {spec.Archetype} has no route at a gate");
            }

            foreach (InfraRoute r in net.Routes)
            {
                // Grades: within the kind's limit, except a frontier road following the land where it could
                // not span a valley, and short ramps to a gate or junction; walkable everywhere.
                for (int i = 0; i + 1 < r.Count; i++)
                {
                    if (r.Span[i] != RoadSpan.Ground) continue;
                    float g = Math.Abs(r.Y[i + 1] - r.Y[i]) / InfraNetwork.Spacing;
                    stretches++;
                    if (g <= InfraNetwork.MaxGrade(r.Kind) * 1.05f) withinGrade++;
                    if (g > 0.6f) { unwalkable++; if (notes.Count < 8) notes.Add($"{r.Kind} {r.Id} on {spec.Archetype} climbs {g:F2} at point {i}"); }
                }
                // The deck is what you walk on, level across: on the centre line and near both edges, away
                // from junctions (another route's deck may cross there), places' pads and the ends.
                for (int i = 8; i + 8 < r.Count; i += 9)
                {
                    if (r.Span[i - 1] != RoadSpan.Ground || r.Span[i] != RoadSpan.Ground) continue;
                    if (net.Routes.Any(o => o != r && o.Count > 1 && Math.Min(Dist(w, o.X[0], o.Z[0], r.X[i], r.Z[i]), Dist(w, o.X[^1], o.Z[^1], r.X[i], r.Z[i])) < 30)) continue;
                    if (net.Routes.Any(o => o != r && o.Kind != r.Kind && Crosses(w, o, r.X[i], r.Z[i], o.HalfWidth + r.HalfWidth + 30))) continue;
                    if (w.Pois.Any(p => p.Stamps.Any(t => t.Weight(Mathx.WrapX(r.X[i], w.Width), Mathx.Wrap(r.Z[i], w.Height)) > 0f))) continue;
                    double dx = r.X[i + 1] - r.X[i - 1], dz = r.Z[i + 1] - r.Z[i - 1], len = Math.Sqrt(dx * dx + dz * dz);
                    double sx = -dz / len, sz = dx / len;
                    float centre = w.Source.SurfaceHeight(Mathx.WrapX(r.X[i], w.Width), Mathx.Wrap(r.Z[i], w.Height));
                    deckSamples++;
                    if (Math.Abs(centre - r.Y[i]) > 0.35f + (r.Kept ? 0 : 0.25f))
                    {
                        offDeck++;
                        if (offDeck <= 6)
                        {
                            double px = Mathx.WrapX(r.X[i], w.Width), pz = Mathx.Wrap(r.Z[i], w.Height);
                            var near = w.Source.StampsAt(px, pz).Select(k => w.Source.Stamps[k]).Where(t => t.Weight(px, pz) > 0 || t.Shape == StampShape.Road && t.NearestOnPath(px, pz, out float d, out _, out _) && d < 40)
                                .Select(t => $"{t.Name}/{t.Shape}{(t.Shape == StampShape.Road && t.NearestOnPath(px, pz, out float d2, out float y2, out RoadSpan sp2) ? $" d{d2:F1} y{y2:F1} {sp2}" : "")}");
                            GD.Print($"INFO    off deck: {r.Kind} {r.Id} point {i} at {px:F0},{pz:F0}: ground {centre:F2}, deck {r.Y[i]:F2}, natural {w.Source.NaturalHeight(px, pz):F2}; stamps here: {string.Join("; ", near)}");
                        }
                    }
                    foreach (double side in new[] { -0.8, 0.8 })
                    {
                        double ex = r.X[i] + sx * r.HalfWidth * side, ez = r.Z[i] + sz * r.HalfWidth * side;
                        double qx = Mathx.WrapX(ex, w.Width), qz = Mathx.Wrap(ez, w.Height);
                        float edge = w.Source.SurfaceHeight(qx, qz);
                        // Where another road's bed is nearer (a switchback's other leg, a road alongside), that road's deck rules.
                        int nearest = TerrainStamp.NearestRoad(w.Source.Stamps, w.Source.StampsAt(qx, qz), qx, qz);
                        bool own = nearest >= 0 && w.Source.Stamps[nearest].NearestOnPath(qx, qz, out _, out float ny, out _) && Math.Abs(ny - r.Y[i]) < 0.6f;
                        if (own && Math.Abs(edge - centre) > 0.5f) tilted++;
                        // Seam-safe: the same ground from every copy of the planet.
                        float copy = w.Source.SurfaceHeight(Mathx.WrapX(ex, w.Width) + w.Width, Mathx.Wrap(ez, w.Height) - w.Height);
                        seamSamples++;
                        if (Math.Abs(copy - edge) > 1e-4f) seamMismatch++;
                    }
                }
            }
            foreach (InfraStructure st in net.Structures)
            {
                InfraRoute r = st.Route;
                if (st.Kind == RoadSpan.Bridge)
                {
                    bridges++;
                    bool wet = false;
                    for (int i = st.First; i <= st.Last; i++)
                    {
                        double x = Mathx.WrapX(r.X[i], w.Width), z = Mathx.Wrap(r.Z[i], w.Height);
                        if (w.Source.WaterSurfaceAt(x, z, out float surface))
                        {
                            wet = true;
                            if (r.Y[i] < surface + 1.2f) lowOverWater++;
                        }
                    }
                    // A dry viaduct no longer than its builders could span (plus the reach to its abutments).
                    if (!st.OverWater && !r.Maglev && st.Length > InfraNetwork.MaxViaduct(r.Kind, r.Tech) + 64) { longViaducts++; if (notes.Count < 10) notes.Add($"a {st.Length:F0} m dry bridge on {r.Kind} {r.Id} ({spec.Archetype})"); }
                }
                else
                {
                    tunnels++;
                    if (st.Length > InfraNetwork.MaxTunnel(r.Kind, r.Tech) + 64) longTunnels++;
                }
            }
            if (a == 0)
            {
                // The same planet plans the same network, point for point.
                PlanetWorld again = PlanetWorld.Build(spec, Main.Catalog);
                same = again.Infra.Routes.Count == net.Routes.Count && again.Infra.Routes.Zip(net.Routes).All(p =>
                    p.First.Count == p.Second.Count && p.First.Kind == p.Second.Kind && p.First.X.SequenceEqual(p.Second.X) && p.First.Y.SequenceEqual(p.Second.Y) && p.First.Span.SequenceEqual(p.Second.Span));
            }
            await Frames(1);
        }
        string km = string.Join(", ", kinds.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value:F0} km"));
        Check(never > 0 && neverRoutes == 0, $"a world never inhabited has no infrastructure ({never} such, {neverRoutes} routes)");
        Check(unrouted == 0 && joined == places && routes > 40,
            $"the network reaches every place it serves at one of its gates: {joined} of {places} places, {routes} routes ({km}), {unrouted} links unrouted, {separated} across a sea never bridged{(notes.Count > 0 ? "; " + string.Join("; ", notes.Take(3)) : "")}");
        Check(withinGrade >= stretches * 0.9 && unwalkable == 0,
            $"grades: {withinGrade} of {stretches} stretches within their kind's limit (the rest frontier roads following the land, or ramps to gates and junctions), {unwalkable} steeper than a walkable 31 degrees");
        Check(longViaducts == 0 && lowOverWater == 0 && longTunnels == 0,
            $"bridges and tunnels within what their builders could do: {bridges} bridges ({longViaducts} dry ones too long, {lowOverWater} points too low over water), {tunnels} tunnels ({longTunnels} too long){(notes.Count > 3 ? "; " + string.Join("; ", notes.Skip(3).Take(3)) : "")}");
        Check(offDeck <= deckSamples / 50 && tilted <= deckSamples / 25,
            $"roads are walkable decks: the ground is at the deck on {deckSamples - offDeck} of {deckSamples} centre-line samples and level across on {2 * deckSamples - tilted} of {2 * deckSamples} edge samples");
        Check(seamMismatch == 0 && seamSamples > 100, $"roads are seam-safe: {seamSamples} road samples read the same from another copy of the planet ({seamMismatch} differ)");
        Check(same, "the same planet plans the same network, point for point");
        GD.Print($"INFO  planning took {planBest:F0} to {planWorst:F0} ms a world");
    }

    static double Dist(PlanetWorld w, double ax, double az, double bx, double bz)
    {
        double dx = Mathx.WrapDelta(ax - bx, w.Width), dz = Mathx.WrapDelta(az - bz, w.Height);
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Whether a route passes within a distance of a point.</summary>
    static bool Crosses(PlanetWorld w, InfraRoute o, double x, double z, double within)
    {
        for (int k = 0; k < o.Count; k += 2)
            if (Dist(w, o.X[k], o.Z[k], x, z) < within) return true;
        return false;
    }
}
