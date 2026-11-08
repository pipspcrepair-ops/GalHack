using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Infra;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using Archetype = Planets.Gen.Archetype;

namespace PlanetSurface;

/// <summary>Junctions as the terrain shader draws them: their kerb corners, what stands round them, and the ground under the roads.</summary>
public partial class SurfaceTest
{
    /// <summary>
    /// On fresh worlds (start towns, a city, roundabouts forced on some): every junction's corners are rounded, and
    /// each kerb (and a street's footpath) runs through its corner in one smooth curve, no step sharper than the corner's
    /// radius allows, nothing crossing it; every roundabout is round and no two junctions' corners run into one another
    /// (no capsule); no place's footprint stands in a junction's corner; every lamp stands in its verge on the ground; the
    /// ground lies at the deck under every corner and turning bulb; nothing under a town breaks the surface; and no land
    /// stands up through a bridge.
    /// </summary>
    async Task KerbChecks()
    {
        var rand = new Random();
        bool wasStart = PlanetSurface.Poi.PoiSiting.StartWorld, wasCity = CityPlan.Force;
        int worlds = 0, corners = 0, traced = 0, rough = 0, broken = 0, kerbed = 0, footRough = 0, missed = 0, gores = 0;
        int rings = 0, oval = 0, capsules = 0, junctions = 0, sightSamples = 0, intruding = 0, lamps = 0, lampsOff = 0;
        int bedSamples = 0, bedOff = 0, underCells = 0, underOut = 0, bridgeSamples = 0, throughDeck = 0, overpasses = 0;
        var notes = new Dictionary<string, string>();
        void Note(string key, string s) { if (!notes.ContainsKey(key)) notes[key] = s; }
        var forms = new Dictionary<string, int>();
        string mapDir = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--kerbmaps="))?["--kerbmaps=".Length..];
        int maps = 0;
        var failing = new List<(double X, double Z, string Why)>();
        try
        {
            var plan = new (Archetype Kind, bool Start, bool City, bool Rings)[]
            {
                (Archetype.Arid, true, false, true), (Archetype.Temperate, true, false, false), (Archetype.Arid, true, false, false),
                (Archetype.Temperate, false, true, false), (Archetype.Arid, false, true, true), (Archetype.Temperate, true, false, true),
            };
            foreach (var (kind, start, city, ringsOn) in plan)
            {
                PlanetSurface.Poi.PoiSiting.StartWorld = start;
                CityPlan.Force = city;
                InfraNetwork.ForceRoundabouts = ringsOn;
                PlanetWorld w = PlanetWorld.Build(PlanetSpec.Roll((ulong)rand.NextInt64(), kind, 0), Main.Catalog);
                InfraNetwork.ForceRoundabouts = false;
                if (w.Infra?.Profile == null || !w.Infra.Profile.Inhabited) continue;
                worlds++;
                InfraNetwork net = w.Infra;
                RoadField field = RoadField.Build(w.Source, w.Width, w.Height, net.Corners);
                foreach (var (why, n) in net.CornerMisses)
                {
                    if (why.EndsWith("too sharp")) gores += n; else { missed += n; Note("missed", why); }
                }
                foreach (var m in net.CornerMissedAt.Where(m => !m.Why.EndsWith("too sharp")))
                {
                    Note("missed " + m.Why, $"{m.X:F0},{m.Z:F0}");
                    if (!failing.Any(f => f.Why == "missed")) failing.Add((m.X, m.Z, "missed"));
                }
                foreach (RoadCorner c in net.Corners)
                {
                    if (c.Disc) continue;
                    corners++;
                    forms[c.Why] = forms.GetValueOrDefault(c.Why) + 1;
                    // The kerb through the corner, and a kerbed street's footpath's outer edge.
                    traced++;
                    string fault = TraceKerb(field, c, 0f, w);
                    if (fault != null)
                    {
                        if (fault.StartsWith("broken")) broken++; else rough++;
                        Note("kerb", $"{c.Why} corner at {c.X:F0},{c.Z:F0} (r {c.R:F1}): {fault}");
                        Note("kerb " + c.Why, $"{c.X:F0},{c.Z:F0} (r {c.R:F1}): {fault}");
                        if (!failing.Any(f => f.Why == $"{c.Why}-kerb")) failing.Add((c.X, c.Z, $"{c.Why}-kerb"));
                    }
                    if (c.Kerbed)
                    {
                        kerbed++;
                        string foot = TraceKerb(field, c, InfraNetwork.FootpathOut, w);
                        if (foot != null)
                        {
                            footRough++;
                            Note("foot", $"{c.Why} corner at {c.X:F0},{c.Z:F0}: {foot}");
                            Note("foot " + c.Why, $"{c.X:F0},{c.Z:F0}: {foot}");
                            if (fault == null && !failing.Any(f => f.Why == $"{c.Why}-foot")) failing.Add((c.X, c.Z, $"{c.Why}-foot"));
                        }
                    }
                    // Its corner kept clear: no place's footprint in the quarter it rounds off (the kerb, the verge, the
                    // footpath and the sight line across it).
                    double cab = c.AX * c.BZ - c.AZ * c.BX;
                    double a0 = Math.Atan2(c.AZ, c.AX), sweep = Math.Acos(Math.Clamp(c.AX * c.BX + c.AZ * c.BZ, -1, 1)) * Math.Sign(cab);
                    for (int i = 0; i <= 6; i++)
                        for (int k = 1; k <= 4; k++)
                        {
                            double ang = a0 + sweep * i / 6, rr = c.R * (0.25 + 0.75 * k / 4.0);
                            double x = Mathx.WrapX(c.X + Math.Cos(ang) * rr, w.Width), z = Mathx.Wrap(c.Z + Math.Sin(ang) * rr, w.Height);
                            sightSamples++;
                            foreach (int s in w.Source.StampsAt(x, z))
                            {
                                TerrainStamp t = w.Source.Stamps[s];
                                if (t.Shape != StampShape.Pad || !t.OnFootprint(x, z)) continue;
                                intruding++;
                                Note("sight", $"{t.Name} stands in the {c.Why} corner at {c.X:F0},{c.Z:F0}");
                                break;
                            }
                        }
                    // The ground lies at the deck under the corner's own deck (between its arc and the kerb lines' corner).
                    for (int i = 1; i < 6; i++)
                    {
                        double ang = a0 + sweep * i / 6, rr = c.R + 0.8;
                        double x = Mathx.WrapX(c.X + Math.Cos(ang) * rr, w.Width), z = Mathx.Wrap(c.Z + Math.Sin(ang) * rr, w.Height);
                        if (w.Source.Built(x, z, out _) > 0.5f) continue;
                        bedSamples++;
                        float g = w.Source.SurfaceHeight(x, z);
                        if (Math.Abs(g - c.Y) > 0.8f) { bedOff++; Note("bed", $"the {c.Why} corner's deck at {x:F0},{z:F0} is {g - c.Y:F2} m off the ground"); }
                    }
                }
                // Turning bulbs: the ground at the deck across the whole disc.
                foreach (InfraRoute r in net.Routes.Where(RoadCorners.Paved))
                    foreach (bool atStart in new[] { true, false })
                    {
                        if ((atStart ? r.StartJoin : r.EndJoin) != JunctionForm.DeadEnd) continue;
                        int e = atStart ? 0 : r.Count - 1;
                        float rb = RoadCorners.BulbRadius(r);
                        for (int i = 0; i < 12; i++)
                            for (float f = 0.3f; f <= 1f; f += 0.35f)
                            {
                                double x = Mathx.WrapX(r.X[e] + Math.Cos(i * Math.Tau / 12) * rb * f, w.Width), z = Mathx.Wrap(r.Z[e] + Math.Sin(i * Math.Tau / 12) * rb * f, w.Height);
                                if (w.Source.Built(x, z, out _) > 0.5f) continue;
                                bedSamples++;
                                float g = w.Source.SurfaceHeight(x, z);
                                if (Math.Abs(g - r.Y[e]) > 0.8f) { bedOff++; Note("bulb", $"a turning bulb's deck at {x:F0},{z:F0} is {g - r.Y[e]:F2} m off the ground"); }
                            }
                    }
                // Rings round, and junctions on one way spaced so their corners never run together.
                foreach (InfraJunction j in net.Joins.Where(j => j.Form == JunctionForm.Roundabout && j.Ring != null))
                {
                    rings++;
                    var ds = Enumerable.Range(0, j.Ring.Count).Select(i => Dist(w, j.Ring.X[i], j.Ring.Z[i], j.X, j.Z)).ToList();
                    if (ds.Max() - ds.Min() > 0.2) { oval++; Note("oval", $"the ring at {j.X:F0},{j.Z:F0} runs {ds.Min():F1} to {ds.Max():F1} m from its middle"); }
                }
                junctions += net.Joins.Count(j => j.Form is JunctionForm.Tee or JunctionForm.Crossroads or JunctionForm.Roundabout);
                for (int a = 0; a < net.Corners.Count; a++)
                    for (int b = a + 1; b < net.Corners.Count; b++)
                    {
                        RoadCorner p = net.Corners[a], q = net.Corners[b];
                        if (p.Disc || q.Disc || Dist(w, p.X, p.Z, q.X, q.Z) > p.Cut + q.Cut) continue;
                        if (ArcCovered(p, q, w) || ArcCovered(q, p, w)) { capsules++; Note("capsule", $"a {p.Why} corner at {p.X:F0},{p.Z:F0} and a {q.Why} corner at {q.X:F0},{q.Z:F0} run into one another"); }
                    }
                foreach (InfraJunction ring in net.Joins.Where(j => j.Form == JunctionForm.Roundabout))
                    foreach (InfraJunction q in net.Joins.Where(j => j != ring && j.Form is JunctionForm.Tee or JunctionForm.Crossroads or JunctionForm.Roundabout))
                    {
                        if (!ring.Arms.Any(x => q.Arms.Any(y => y.Route == x.Route))) continue;
                        double d = Dist(w, ring.X, ring.Z, q.X, q.Z);
                        if (d < ring.RingR + ring.RingHw + 18) { capsules++; Note("capsule", $"a {q.Form} {d:F0} m from a roundabout's middle at {ring.X:F0},{ring.Z:F0}"); }
                    }
                // Lamps stand upright in a verge, on the ground: never on a deck or a junction's corner.
                foreach (StreetItem item in net.Furniture.Where(f => f.Thing == StreetThing.Lamp))
                {
                    lamps++;
                    float g = w.Source.SurfaceHeight(item.X, item.Z);
                    float edge = field.DeckEdgeAt(item.X, item.Z, g, 1.2f);
                    if (Math.Abs(g - item.Y) > 0.25f || edge < 0.3f || edge > InfraNetwork.FootpathOut)
                    {
                        lampsOff++;
                        InfraRoute near = net.Nearest(item.X, item.Z, 20, out int ni, out double nd);
                        Note("lamp", $"a lamp at {item.X:F0},{item.Z:F0}: {item.Y - g:F2} m off the ground, {edge:F2} m past the kerb (the nearest way a {near?.Kind} hw {near?.HalfWidth:F1}{(near?.Ring == true ? " ring" : "")} {nd:F1} m off, its point {ni} of {near?.Count}; off the decks by the planner: {net.OffDeck(item.X, item.Z, 0.4)})");
                        if (!failing.Any(f => f.Why == "lamp")) failing.Add((item.X, item.Z, "lamp"));
                    }
                }
                // Nothing under a town breaks the surface (its walls and roofs drawn in the dark show black above ground).
                if (net.Town?.Under?.Built is { } built)
                {
                    // The top of each column of it: a block's top face under the ground, an open cell a little deeper.
                    var tops = new Dictionary<(int, int), float>();
                    foreach (PoiGenerator.Gen.Int3 cell in built.Blocks.Keys.Concat(built.Open))
                    {
                        if (built.Daylight.Contains(cell)) continue;
                        bool near = false;
                        foreach (var d in new[] { (0, 1, 0), (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1), (0, 2, 0) })
                            near |= built.Daylight.Contains(new PoiGenerator.Gen.Int3(cell.X + d.Item1, cell.Y + d.Item2, cell.Z + d.Item3));
                        if (near) continue;
                        float need = cell.Y + (built.Blocks.ContainsKey(cell) ? 0.95f : 1.4f);
                        var key = (cell.X, cell.Z);
                        tops[key] = tops.TryGetValue(key, out float top) ? Math.Max(top, need) : need;
                    }
                    // (Not by its mouths, stairs and culverts, where the ground is their pads' and it is meant to come up.)
                    var lit = new HashSet<(int, int)>(built.Daylight.Select(d => (d.X >> 3, d.Z >> 3)));
                    foreach (var ((cx, cz), need) in tops)
                    {
                        bool byMouth = false;
                        for (int ox = -1; ox <= 1 && !byMouth; ox++)
                            for (int oz = -1; oz <= 1 && !byMouth; oz++) byMouth = lit.Contains(((cx >> 3) + ox, (cz >> 3) + oz));
                        if (byMouth) continue;
                        underCells++;
                        float g = w.Source.SurfaceHeight(Mathx.WrapX(cx + 0.5, w.Width), Mathx.Wrap(cz + 0.5, w.Height));
                        if (g < need) { underOut++; Note("under", $"the underground at {cx},{cz} needs the ground at {need:F1} and it lies at {g:F1}"); }
                    }
                }
                // No land through a bridge's deck: under it the ground lies at least clear of the deck's underside.
                foreach (InfraStructure s in net.Structures.Where(s => s.Kind == RoadSpan.Bridge && !s.Route.Maglev))
                    for (int k = s.First + 4; k < s.Last - 3; k++)
                    {
                        if (s.Fallen.Contains(k) || s.Fallen.Contains(k - 1)) continue;
                        // (Where another way passes under, its own deck is the ground: an overpass, counted on its own.)
                        if (net.Nearest(s.Route.X[k], s.Route.Z[k], 14, out _, out double dn, new[] { s.Route }) is { } below && dn < below.HalfWidth + 6) { overpasses++; continue; }
                        bridgeSamples++;
                        float g = w.Source.SurfaceHeight(Mathx.WrapX(s.Route.X[k], w.Width), Mathx.Wrap(s.Route.Z[k], w.Height));
                        if (g > s.Route.Y[k] - 1.6f) { throughDeck++; Note("bridge", $"land {g - s.Route.Y[k]:F1} m from a {s.Route.Kind} bridge's deck at {s.Route.X[k]:F0},{s.Route.Z[k]:F0}"); }
                    }
                if (mapDir != null && maps < 24) maps += KerbMaps(w, field, mapDir, maps, failing);
                failing.Clear();
                await Frames(1);
            }
        }
        finally
        {
            PlanetSurface.Poi.PoiSiting.StartWorld = wasStart;
            CityPlan.Force = wasCity;
            InfraNetwork.ForceRoundabouts = false;
        }
        // One rule for every laid or worn surface the terrain shader draws: it gives only its own material and how much of
        // it shows; wherever it thins, the land's own ground shows through. None of them paints a grass or land colour
        // of its own (a track's crown, a weedy edge, a missing slab, the land in a crack all let the land's own ground show).
        var painted = new List<string>();
        string shader = System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://shaders/terrain.gdshader"));
        foreach (string fn in new[] { "vec3 soil_way(", "vec4 paint_road(", "vec4 paint_ground(" })
        {
            int at = shader.IndexOf(fn, StringComparison.Ordinal), open = shader.IndexOf('{', at), depth = 0, end = open;
            for (; end < shader.Length; end++) { if (shader[end] == '{') depth++; else if (shader[end] == '}' && --depth == 0) break; }
            foreach (string raw in shader[open..end].Split((char)10))
            {
                string line = raw.Split("//")[0];
                string rest = System.Text.RegularExpressions.Regex.Replace(line, @"bared\(land\)|greenness\(land\)|dot\(land,|return vec4\(land, 0\.0\)|soil_way\(r, p, land,", "");
                if (System.Text.RegularExpressions.Regex.IsMatch(rest, @"\bland\b")) painted.Add($"{fn.Split(' ')[1].TrimEnd('(')}: {line.Trim()}");
            }
        }
        string N(string key) => notes.TryGetValue(key, out string s) ? $" (first: {s})" : "";
        Check(painted.Count == 0, $"laid and worn surfaces paint only their own material, the land's own ground showing where they thin: {painted.Count} lines painting a colour of the land's{(painted.Count > 0 ? " (" + string.Join("; ", painted.Take(3)) + ")" : "")}");
        string missedWhere = string.Join("; ", notes.Where(n => n.Key.StartsWith("missed ")).Select(n => $"{n.Key[7..]} at {n.Value}"));
        Check(corners > 50 && missed * 10 <= corners, $"junction corners are rounded: {corners} on {worlds} fresh worlds ({string.Join(", ", forms.OrderByDescending(f => f.Value).Select(f => $"{f.Value} {f.Key}"))}), {missed} with no room for one (at most one in ten){(missedWhere.Length > 0 ? " (" + missedWhere + ")" : "")}; {gores} too sharp to round (a slip's gore)");
        string byKind = string.Join("; ", notes.Where(n => n.Key.StartsWith("kerb ") || n.Key.StartsWith("foot ")).Select(n => $"{n.Key}: {n.Value}"));
        Check(traced > 0 && (rough + broken) * 10 <= traced && footRough * 10 <= kerbed + 1, $"kerbs run smooth round every corner: {traced} kerbs traced through their corners ({kerbed} with a footpath), {rough} turning sharper than the corner allows, {broken} broken or crossed; {footRough} footpaths not smooth (at most one in ten of each, the rest listed: ways running on into one another at an angle, bulbs on bending streets){(byKind.Length > 0 ? " (" + byKind + ")" : "")}");
        Check(oval == 0 && capsules == 0, $"roundabouts are round and junctions keep apart: {rings} rings, {oval} not round{N("oval")}; {junctions} junctions, {capsules} pairs so close their corners run together{N("capsule")}");
        Check(sightSamples > 0 && intruding == 0, $"junction corners are clear of places: {sightSamples} points in the corners, {intruding} under a place's footprint{N("sight")}");
        Check(lamps > 20 && lampsOff * 100 <= lamps, $"every lamp stands in its verge on the ground: {lamps} lamps, {lampsOff} not (at most one in a hundred){N("lamp")}");
        Check(bedSamples > 100 && bedOff <= bedSamples / 40 && underOut == 0 && throughDeck * 100 <= bridgeSamples,
            $"the ground meets the road and nothing shows through: {bedSamples} points under corners and turning bulbs, {bedOff} off the deck by over 0.8 m (at most one in forty){N("bed")}{N("bulb")}; {underCells} columns over the underground, {underOut} out of the ground{N("under")}; {bridgeSamples} points under bridges, {throughDeck} with land through the deck (at most one in a hundred){N("bridge")} ({overpasses} points over another way, not judged)");
    }

    /// <summary>Whether one corner's deck runs over the other's kerb arc.</summary>
    static bool ArcCovered(RoadCorner deck, RoadCorner kerb, PlanetWorld w)
    {
        double a0 = Math.Atan2(kerb.AZ, kerb.AX), cab = kerb.AX * kerb.BZ - kerb.AZ * kerb.BX;
        double sweep = Math.Acos(Math.Clamp(kerb.AX * kerb.BX + kerb.AZ * kerb.BZ, -1, 1)) * Math.Sign(cab);
        for (int i = 0; i <= 12; i++)
        {
            double ang = a0 + sweep * i / 12;
            if (deck.Edge(kerb.X + Math.Cos(ang) * kerb.R, kerb.Z + Math.Sin(ang) * kerb.R, w.Width, w.Height) < -0.1f) return true;
        }
        return false;
    }

    /// <summary>How much of a way round a junction its corners take, m: two junctions on one way need this each between them.</summary>
    static double Clearance(InfraJunction j)
    {
        if (j.Form == JunctionForm.Roundabout) return j.RingR + j.RingHw + 6;
        float hw = j.Arms.Max(a => a.Route.HalfWidth);
        float r = j.Arms.Max(a => j.Arms.Where(b => b != a).Select(b => RoadCorners.Radius(a.Route, b.Route)).DefaultIfEmpty(0f).Max());
        return hw + r * 0.6 + 2;
    }

    /// <summary>
    /// Follows the kerb (the deck's edge at <paramref name="level"/> metres past it) from 6 m out along one way, round the
    /// corner, to 6 m out along the other: null if it runs smooth, else what's wrong.
    /// </summary>
    static string TraceKerb(RoadField field, RoadCorner c, float level, PlanetWorld w)
    {
        double rr = c.R - level;
        if (rr < 0.4) return null;
        double W = w.Width, H = w.Height;
        float F(double x, double z) => field.DeckEdgeAt(Mathx.WrapX(x, W), Mathx.Wrap(z, H), c.Y, 3f);
        (double X, double Z) Grad(double x, double z)
        {
            const double e = 0.02;
            return ((F(x + e, z) - F(x - e, z)) / (2 * e), (F(x, z + e) - F(x, z - e)) / (2 * e));
        }
        bool Project(ref double x, ref double z)
        {
            for (int it = 0; it < 6; it++)
            {
                float f = F(x, z) - level;
                if (float.IsInfinity(f)) return false;
                if (Math.Abs(f) < 0.004) return true;
                var (gx, gz) = Grad(x, z);
                double g2 = gx * gx + gz * gz;
                if (g2 < 1e-6) return false;
                x -= gx * f / g2;
                z -= gz * f / g2;
            }
            return Math.Abs(F(x, z) - level) < 0.03;
        }
        // Where the arc at this level meets each kerb line, and the way out along each.
        double tax = c.X + c.AX * rr, taz = c.Z + c.AZ * rr, tbx = c.X + c.BX * rr, tbz = c.Z + c.BZ * rr;
        double oax = -c.AZ, oaz = c.AX;
        if (oax * (c.BX - c.AX) + oaz * (c.BZ - c.AZ) > 0) { oax = -oax; oaz = -oaz; }
        double obx = -c.BZ, obz = c.BX;
        if (obx * (c.AX - c.BX) + obz * (c.AZ - c.BZ) > 0) { obx = -obx; obz = -obz; }
        // (The corner's own: its arc, and a metre and a half of each kerb line it meets; the ways' own lines are checked elsewhere.)
        double runA = c.RoundA ? 1 : Math.Clamp(c.LenA - c.TanA - 1, 0.5, 1.5), runB = c.RoundB ? 1 : Math.Clamp(c.LenB - c.TanB - 1, 0.5, 1.5);
        double px = tax + oax * runA, pz = taz + oaz * runA, ex = tbx + obx * runB, ez = tbz + obz * runB;
        if (!Project(ref px, ref pz)) return $"broken: no kerb {runA} m out along the first way";
        double dx = -oax, dz = -oaz, arc = Math.Acos(Math.Clamp(c.AX * c.BX + c.AZ * c.BZ, -1, 1)) * rr;
        const double Step = 0.2;
        var turns = new Queue<double>();
        double window = 0, most = 0, allowed = 1.0 / rr * 1.3 + 0.13;
        for (double run = 0; run < arc + runA + runB + 6; run += Step)
        {
            var (gx, gz) = Grad(px, pz);
            double gl = Math.Sqrt(gx * gx + gz * gz);
            if (gl < 1e-6) return $"broken at {px:F1},{pz:F1}";
            double tx = -gz / gl, tz = gx / gl;
            if (tx * dx + tz * dz < 0) { tx = -tx; tz = -tz; }
            double turn = Math.Acos(Math.Clamp(tx * dx + tz * dz, -1, 1));
            if (run > 0)
            {
                turns.Enqueue(turn);
                window += turn;
                if (turns.Count > 5) window -= turns.Dequeue();
                most = Math.Max(most, window);
                if (window > allowed) return $"turns {window * 180 / Math.PI:F0} degrees in a metre at {px:F1},{pz:F1} (the corner allows {allowed * 180 / Math.PI:F0})";
            }
            double nx = px + tx * Step, nz = pz + tz * Step;
            if (!Project(ref nx, ref nz)) return $"broken at {nx:F1},{nz:F1}";
            if (Math.Sqrt((nx - px) * (nx - px) + (nz - pz) * (nz - pz)) > Step * 2.5) return $"broken: a step at {px:F1},{pz:F1}";
            dx = tx; dz = tz; px = nx; pz = nz;
            if (Math.Sqrt(Mathx.WrapDelta(px - ex, W) * Mathx.WrapDelta(px - ex, W) + Mathx.WrapDelta(pz - ez, H) * Mathx.WrapDelta(pz - ez, H)) < Step * 1.5) return null;
        }
        return $"broken: it never reaches the other way (lost near {px:F1},{pz:F1})";
    }

    /// <summary>Top-down maps of a few junctions as the shader would draw them (deck, kerb, verge, footpath): for looking at, not checked.</summary>
    static int KerbMaps(PlanetWorld w, RoadField field, string dir, int index, List<(double X, double Z, string Why)> failing)
    {
        System.IO.Directory.CreateDirectory(dir);
        int made = 0;
        var spots = failing.Take(7).ToList();
        if (spots.Count == 0)
            spots = w.Infra.Joins.Where(j => j.Form is JunctionForm.Tee or JunctionForm.Crossroads or JunctionForm.Roundabout)
                .OrderBy(j => j.Form == JunctionForm.Roundabout ? 0 : j.Form == JunctionForm.Crossroads ? 1 : 2).Take(2).Select(j => (j.X, j.Z, j.Form.ToString())).ToList();
        foreach (var spot in spots)
        {
            var j = new InfraJunction { X = spot.X, Z = spot.Z };
            const int Size = 400;
            const double Px = 0.2;
            var img = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    double wx = Mathx.WrapX(j.X + (x - Size / 2) * Px, w.Width), wz = Mathx.Wrap(j.Z + (y - Size / 2) * Px, w.Height);
                    float g = w.Source.SurfaceHeight(wx, wz);
                    float e = field.DeckEdgeAt(wx, wz, g, 1.2f);
                    Color col = e < 0 ? new Color(0.2f, 0.2f, 0.22f) : e < 0.22f ? new Color(0.95f, 0.95f, 0.9f) : e < 1.5f ? new Color(0.35f, 0.55f, 0.3f) : e < InfraNetwork.FootpathOut ? new Color(0.6f, 0.6f, 0.58f) : new Color(0.45f, 0.6f, 0.35f);
                    if (w.Source.Built(wx, wz, out _) > 0.5f && e > 0) col = col.Lerp(new Color(0.8f, 0.4f, 0.3f), 0.5f);
                    img.SetPixel(x, y, col);
                }
            foreach (RoadCorner c in w.Infra.Corners)
            {
                int x = (int)Math.Round(Mathx.WrapDelta(c.X - j.X, w.Width) / Px) + Size / 2, y = (int)Math.Round(Mathx.WrapDelta(c.Z - j.Z, w.Height) / Px) + Size / 2;
                if (x >= 1 && y >= 1 && x < Size - 1 && y < Size - 1)
                {
                    img.SetPixel(x, y, new Color(1, 0, 0));
                    GD.Print($"KERBMAP   {c.Why} corner at px {x},{y}: r {c.R:F1}, a ({c.AX:F2},{c.AZ:F2}) b ({c.BX:F2},{c.BZ:F2}), tangents {c.TanA:F1} / {c.TanB:F1} m, routes {c.RouteA}/{c.RouteB}");
                }
            }

            string path = System.IO.Path.Combine(dir, $"map{index + made}-{spot.Why}.png");
            img.SavePng(path);
            GD.Print($"KERBMAP {path}: {spot.Why} at {j.X:F0},{j.Z:F0}");
            made++;
        }
        return made;
    }
}
