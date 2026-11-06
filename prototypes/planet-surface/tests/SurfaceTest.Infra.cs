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
        string firstGround = "", firstBlocked = "";
        int wreckSites = 0, wreckPieces = 0, blockedLanes = 0, wreckOnPad = 0;
        var wreckKinds = new Dictionary<string, int>();
        int ruins = 0, ruinWorlds = 0, ruinOnRoad = 0, ruinOnPad = 0, ruinsTouching = 0, ruinsYielding = 0, ruinsSpawned = 0, fewRuins = 0, blastedRuins = 0;
        var ruinKinds = new Dictionary<string, int>();
        int tells = 0, glowingTells = 0, tellsChecked = 0;
        int poles = 0, fallen = 0, spans = 0, hanging = 0, unanchored = 0, throughGround = 0, offRoute = 0, onBed = 0, lowWorldLines = 0;
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
            // Power lines: every wire anchored (strung between two standing poles, or hanging from the one
            // that holds it), never through the ground; poles only beside the roads they follow, never on a
            // road bed or a place's pad; on a frontier world only out to mines and works.
            PowerLines power = net.Power;
            foreach (PowerPole pole in power.Poles)
            {
                poles++;
                if (pole.Fallen) fallen++;
                double px = Mathx.WrapX(pole.X, w.Width), pz = Mathx.Wrap(pole.Z, w.Height);
                if (w.Source.StampWeight(px, pz) > 0.05f) onBed++;
            }
            foreach (var (route, firstPole, lastPole) in power.Runs)
            {
                if (route.Kind is RouteKind.Track or RouteKind.Rail) offRoute++;
                if (w.Profile.Before == TechLevel.Low && !new[] { route.From, route.To }.Any(e => e != null && (e.Family.Tags.Contains("mine") || e.Family.Tags.Contains("industry")))) lowWorldLines++;
                for (int k = firstPole; k <= lastPole; k++)
                {
                    PowerPole pole = power.Poles[k];
                    double best = double.MaxValue;
                    for (int i = 0; i < route.Count; i++) best = Math.Min(best, Dist(w, route.X[i], route.Z[i], pole.X, pole.Z));
                    if (best > route.HalfWidth + (pole.Pylon ? 18f : 3.5f) + InfraNetwork.Spacing) offRoute++;
                }
            }
            foreach (PowerWire wire in power.Wires)
            {
                PowerPole pa = power.Poles[wire.A], pb = power.Poles[wire.B];
                int n = power.WiresOn(pa);
                var anchor = pa.Anchor(wire.K, n);
                if (pa.Fallen || wire.Spanned && pb.Fallen) unanchored++;
                List<System.Numerics.Vector3> pts = power.Path(wire, (x, z) => w.Source.SurfaceHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height)));
                if (System.Numerics.Vector3.Distance(pts[0], anchor) > 1e-3f) unanchored++;
                if (wire.Spanned)
                {
                    spans++;
                    if (System.Numerics.Vector3.Distance(pts[^1], pb.Anchor(wire.K, n)) > 1e-3f) unanchored++;
                }
                else hanging++;
                // Clear of the ground all along (a hanging wire lies on it at the pole's foot).
                foreach (var q in pts.Skip(1).SkipLast(1))
                {
                    float g = w.Source.SurfaceHeight(Mathx.WrapX(q.X, w.Width), Mathx.Wrap(q.Z, w.Height));
                    if (q.Y < g - 0.01f || !wire.Spanned && q.Y > g + 0.2f && q != pts[1])
                    {
                        throughGround++;
                        if (firstGround.Length == 0) firstGround = $" (first: a {(wire.Spanned ? "strung" : "hanging")} wire at {q.X:F0}, {q.Y:F1}, {q.Z:F0} over ground at {g:F1})";
                        break;
                    }
                }
            }
            // Abandoned vehicles: each site dressed by the library keeps its lane open end to end (a walker
            // or driver of the lane's width gets through), and none stands on a place's pad.
            foreach (WreckSite site in net.Wrecks)
            {
                wreckSites++;
                wreckKinds[site.Site] = wreckKinds.GetValueOrDefault(site.Site) + 1;
                for (float sz = 0; sz <= site.Length; sz += 4)
                for (float sx = 0; sx <= site.Width; sx += 4)
                {
                    var (px, pz) = site.World(sx, sz);
                    double cx = Mathx.WrapX(px, w.Width), cz = Mathx.Wrap(pz, w.Height);
                    if (TerrainStamp.OnPad(w.Source.Stamps, w.Source.StampsAt(cx, cz), cx, cz)) { wreckOnPad++; goto nextCheck; }
                }
                nextCheck:
                if (wreckSites > 40) continue;   // dressing every site is slow: the first forty are enough
                GalHack.Deco.DecoPlan plan = InfraDeco.DressSite(site.Site, site.Seed, site.Tech);
                if (plan == null) continue;
                wreckPieces += plan.Placements.Count;
                if (!LaneOpen(plan, site)) { blockedLanes++; if (firstBlocked.Length == 0) firstBlocked = $" (first: a {site.Site} on {spec.Archetype})"; }
            }
            // Flavour ruins: plenty of them, none on a road bed, a place's pad or another ruin; each spawns,
            // and yields salvage.
            ruinWorlds++;
            if (net.Ruins.Count < 10) fewRuins++;
            foreach (FlavourRuin ruin in net.Ruins)
            {
                ruins++;
                ruinKinds[ruin.Family.Id] = ruinKinds.GetValueOrDefault(ruin.Family.Id) + 1;
                if (ruin.Blast > 0.05f) blastedRuins++;
                TerrainStamp t = ruin.Stamp;
                for (double z = t.MinZ - 2; z <= t.MaxZ + 2; z += 3)
                for (double x = t.MinX - 2; x <= t.MaxX + 2; x += 3)
                {
                    double cx = Mathx.WrapX(x, w.Width), cz = Mathx.Wrap(z, w.Height);
                    var r = net.Nearest(cx, cz, 40, out int ri, out double rd);
                    if (r != null && rd < r.HalfWidth + 1.5) { ruinOnRoad++; goto checkedRoad; }
                }
                checkedRoad:
                foreach (PoiSite p in w.Pois)
                    if (p.Stamp.Weight(t.CenterX, t.CenterZ) > 0f) { ruinOnPad++; break; }
                foreach (FlavourRuin o in net.Ruins)
                    if (o != ruin && o.Stamp.Weight(t.CenterX, t.CenterZ) >= 1f) { ruinsTouching++; break; }
            }
            foreach (FlavourRuin ruin in net.Ruins.Take(3))
            {
                PoiInstance inst = ruin.Spawn(w.Profile);
                ruinsSpawned++;
                var y = FlavourRuin.Yields(inst);
                if (y.GetValueOrDefault("rubble") > 0 && y.Values.Sum() > 5) ruinsYielding++;
            }
            // Ore tells: a formation over every deposit not under a pad, a road or water; veins matte, never neon.
            var tellPieces = PlanetSurface.Infra.InfraLayer.OrePieces(w).ToList();
            tells += tellPieces.Count;
            foreach (Deposit d in w.Map.Deposits.InRect(0, 0, w.Width, w.Height).Take(12))
            {
                tellsChecked++;
                var mesh = GalHack.Deco.DecoOreTells.Build(PlanetSurface.Infra.InfraLayer.TellOf(d), new GalHack.Deco.DecoRgb(1, 0, 0), Planets.Look.DepositLook.Glow(d.Kind) * 0.3f, false, 7, 2f);
                if (mesh.Surfaces[(int)GalHack.Deco.DecoSurface.Glow].Positions.Count > 0) glowingTells++;
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
        Check(wreckSites > 10 && wreckPieces > 100 && blockedLanes == 0 && wreckOnPad == 0,
            $"abandoned vehicles keep the way open: {wreckSites} sites ({string.Join(", ", wreckKinds.OrderBy(k => k.Key).Select(k => $"{k.Value} {k.Key}"))}), {wreckPieces} pieces in the forty dressed; {blockedLanes} with the lane blocked, {wreckOnPad} on a place's pad{firstBlocked}");
        Check(ruins > 100 && fewRuins == 0 && ruinOnRoad == 0 && ruinOnPad == 0 && ruinsTouching == 0 && ruinsYielding == ruinsSpawned,
            $"flavour ruins: {ruins} on {ruinWorlds} inhabited worlds ({string.Join(", ", ruinKinds.OrderBy(k => k.Key).Select(k => $"{k.Value} {k.Key}"))}; {blastedRuins} flattened by a blast), every world with ten or more ({fewRuins} with fewer); {ruinOnRoad} on a road bed, {ruinOnPad} on a place's pad, {ruinsTouching} on another ruin; {ruinsYielding} of {ruinsSpawned} spawned yield salvage");
        Check(tells > 1000 && glowingTells == 0, $"ore tells: {tells} formations over the deposits, their veins matte ({glowingTells} of {tellsChecked} sampled glow)");
        Check(poles > 100 && spans > 100 && unanchored == 0 && throughGround == 0,
            $"power lines are anchored: {poles} poles and pylons ({fallen} fallen), {spans} wires strung between standing poles, {hanging} hanging from the pole that holds them; {unanchored} unanchored, {throughGround} through the ground or lying off it{firstGround}");
        Check(offRoute == 0 && onBed == 0 && lowWorldLines == 0,
            $"power lines only where they make sense: every pole beside the road or highway it follows ({offRoute} not), none on a road bed or a pad ({onBed}), and on frontier worlds only out to mines and works ({lowWorldLines} elsewhere)");
        GD.Print($"INFO  planning took {planBest:F0} to {planWorst:F0} ms a world");
    }

    /// <summary>
    /// Whether a dressed site's lane runs open end to end: every piece's footprint (its mesh's box,
    /// turned and placed) marked on a half-metre raster, grown by half the lane, and a way found from
    /// the north opening to the south one. Pieces you walk over (rails, scars, litter) don't count.
    /// </summary>
    static bool LaneOpen(GalHack.Deco.DecoPlan plan, WreckSite site)
    {
        const float Cell = 0.5f;
        int nx = (int)Math.Ceiling(site.Width / Cell), nz = (int)Math.Ceiling(site.Length / Cell);
        var blocked = new bool[nx * nz];
        var forge = InfraDeco.Poi.Forge;
        foreach (var pl in plan.Placements)
        {
            var piece = forge.Catalogue.Piece(pl.Piece);
            if (piece.WalkOver) continue;
            var mesh = forge.Mesh(pl.Look);
            if (mesh.Max.Y - mesh.Min.Y < 0.35f) continue;
            var t = pl.Transform;
            var c = new[] { new System.Numerics.Vector2(mesh.Min.X, mesh.Min.Z), new(mesh.Max.X, mesh.Min.Z), new(mesh.Max.X, mesh.Max.Z), new(mesh.Min.X, mesh.Max.Z) }
                .Select(v => { var p = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(v.X, 0, v.Y), t); return new System.Numerics.Vector2(p.X, p.Z); }).ToArray();
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                var q = new System.Numerics.Vector2((i + 0.5f) * Cell, (j + 0.5f) * Cell);
                bool inside = true;
                for (int e = 0; e < 4 && inside; e++)
                {
                    var a0 = c[e];
                    var a1 = c[(e + 1) % 4];
                    float cross = (a1.X - a0.X) * (q.Y - a0.Y) - (a1.Y - a0.Y) * (q.X - a0.X);
                    inside = cross >= 0;
                }
                if (!inside)
                {
                    inside = true;
                    for (int e = 0; e < 4 && inside; e++)
                    {
                        var a0 = c[e];
                        var a1 = c[(e + 1) % 4];
                        float cross = (a1.X - a0.X) * (q.Y - a0.Y) - (a1.Y - a0.Y) * (q.X - a0.X);
                        inside = cross <= 0;
                    }
                }
                if (inside) blocked[j * nx + i] = true;
            }
        }
        // Grow by half the lane: the walker's centre must keep that far from anything.
        int grow = (int)Math.Ceiling(site.Lane / 2 / Cell - 0.01f);
        var open = new bool[nx * nz];
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            bool ok = i >= grow - 1 && i < nx - grow + 1;
            for (int dj = -grow + 1; dj < grow && ok; dj++)
            for (int di = -grow + 1; di < grow && ok; di++)
            {
                int x = i + di, z = j + dj;
                if (x < 0 || x >= nx) { ok = false; break; }
                if (z < 0 || z >= nz) continue;
                ok = !blocked[z * nx + x];
            }
            open[j * nx + i] = ok;
        }
        var queue = new Queue<int>();
        var seen = new bool[nx * nz];
        int laneCell = (int)(site.LaneX / Cell);
        for (int i = Math.Max(0, laneCell - 2); i <= Math.Min(nx - 1, laneCell + 2); i++)
            if (open[i]) { queue.Enqueue(i); seen[i] = true; }
        while (queue.Count > 0)
        {
            int k = queue.Dequeue();
            int i = k % nx, j = k / nx;
            if (j == nz - 1) return true;
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int x = i + di, z = j + dj;
                if (x < 0 || x >= nx || z < 0 || z >= nz) continue;
                int m = z * nx + x;
                if (seen[m] || !open[m]) continue;
                seen[m] = true;
                queue.Enqueue(m);
            }
        }
        return false;
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
