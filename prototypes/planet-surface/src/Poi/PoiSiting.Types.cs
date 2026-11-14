using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using PoiGenerator.Gen;
using TerrainDigging.Terrain;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace PlanetSurface.Poi;

/// <summary>
/// A place of a type (data/place-types.txt) on a world: its sketch, the quarter turn and spot the ground chose, the
/// level it was graded to, the one stamp that grades it, and its parts, each a place of its own laid out when first wanted.
/// Its runway, taxiways, apron and car park are the world's made ground (<see cref="Grounds"/>), never blocks.
/// </summary>
public sealed class TypedPlace
{
    public Sketch Sketch;
    public int Turn;
    /// <summary>World metres of the sketch's middle (canonical), and the level it was graded to.</summary>
    public double X, Z;
    public int PadY;
    /// <summary>The whole of it levelled, with a long easing out to the land (no wall, no pit at its edge).</summary>
    public TerrainStamp Stamp;
    public readonly List<PoiSite> Parts = new();
    /// <summary>The part the world's road comes to: its gate is the place's.</summary>
    public PoiSite Anchor;
    public string Why = "";

    /// <summary>A box of the turned sketch in world metres.</summary>
    public (double X0, double Z0, double X1, double Z1) World(SketchBox b) => (X + b.X0, Z + b.Z0, X + b.X1, Z + b.Z1);

    /// <summary>
    /// Its laid ground as the terrain shader paints it: the runway over its shoulders with its markings, each pad in its
    /// blast apron (a recessed one on its pit's floor), the taxiways and links with theirs (a link painted on down its
    /// pad's ramp), the apron in slabs, the car park in bays, a plaza in flags, the access road; worn by the place's age.
    /// </summary>
    public IEnumerable<GroundPatch> Grounds(float wear)
    {
        float deepest = Sketch.Items.Max(PlaceTypes.Sink);
        foreach (var (item, sh) in PlaceTypes.Grounds(Sketch, Turn))
        {
            MadeCover cover = item.Part.Kind == "rail" ? MadeCover.Gravel
                : item.Part.What switch { "tarmac" => MadeCover.Tarmac, "concrete" => MadeCover.Concrete, "gravel" => MadeCover.Gravel, "flags" => MadeCover.Flags, _ => MadeCover.Dirt };
            bool pad = item.Part.Kind == "pads";
            float shoulder = item.Rolled.GetValueOrDefault(pad ? "apron" : "shoulder");
            // (A strip or taxiway of earth or gravel keeps its pattern: graded straight, the earth one rutted and marked.)
            GroundPattern pattern = item.Part.Id switch
            {
                _ when pad => GroundPattern.Pad,
                "runway" or "crosswind" => GroundPattern.Runway,
                "taxiway" or "link" or "spoke" or "spine" or "lane" or "ring" => GroundPattern.Taxiway,
                "carpark" => cover is MadeCover.Tarmac or MadeCover.Concrete ? GroundPattern.Bays : GroundPattern.Plain,
                _ => cover == MadeCover.Concrete ? GroundPattern.Slabs : GroundPattern.Plain,
            };
            // The higher wins where they overlap: the runway's markings run on across the crosswind's end, the crosswind's
            // laid width on across the runway's shoulders; a pad over the links and taxiways that run into it.
            int layer = pad ? 4 : item.Part.Id switch { "runway" => 7, "crosswind" => 6, "taxiway" or "spoke" or "spine" or "lane" or "ring" => 3, "link" => 2, _ => 1 };
            GroundPatch Patch(Shape at, MadeCover c, GroundPattern pat, float w, int l) => new()
            {
                X = X + at.X, Z = Z + at.Z, UX = at.UX, UZ = at.UZ, HalfU = at.HalfU, HalfV = at.HalfV, Round = MathF.Max(0.5f, at.Round),
                Y = PadY - PlaceTypes.Sink(item), Cover = c, Pattern = pat, Wear = w, Layer = l, Slab = c == MadeCover.Concrete ? 7.5f : 5f,
                Give = item.Part.Id == "link" && deepest > 0f ? deepest + 1f : 0f,
            };
            Shape inner = sh;
            if (shoulder > 0f && cover != MadeCover.Dirt)
            {
                // The shoulders: the full width, older-looking and plain, under the runway proper; a pad's blast apron all
                // round it, in slabs.
                yield return Patch(sh, pad ? MadeCover.Concrete : MadeCover.Gravel, pad ? GroundPattern.Slabs : GroundPattern.Plain, Math.Min(1f, wear + 0.15f), layer - 2);
                inner = sh with { HalfV = sh.HalfV - shoulder, HalfU = pad ? sh.HalfU - shoulder : sh.HalfU };
            }
            yield return Patch(inner, cover, pattern, wear, layer);
        }
    }
}

public static partial class PoiSiting
{
    /// <summary>The steepest a typed place's ground may run between two samples, rise over run (a cliff across it rules a spot out).</summary>
    const float TypedCliff = 0.3f;
    /// <summary>The most a typed place may cut or fill, m.</summary>
    const float TypedCutFill = 30f;
    /// <summary>The steepest its grading's bank may run, rise over run.</summary>
    const float TypedBank = 0.2f;

    /// <summary>Tests: what the last world's typed places cost to site, ms, and how many there were.</summary>
    public static double LastTypedMs;
    public static int LastTyped;
    /// <summary>Tests and screenshots: the type a typed family is planned as, whatever its world's age.</summary>
    public static string ForceType;
    /// <summary>Tests: why the last typed place's spots were turned down.</summary>
    public static string LastTypedRejects = "";

    /// <summary>
    /// The world's ground at a coarse step, made once per world and shared by every typed place: running sums over 64 m
    /// cells of the land's height and its square, of wet cells (sea, or a river wider than <see cref="Culvert"/>) and of
    /// steep ones, so any box's mean, spread, wetness and cliffs read in a few lookups.
    /// </summary>
    sealed class CoarseGround
    {
        public readonly int W, H;
        public readonly double Cell;
        readonly double[] sum, sq;
        readonly int[] wet, steep;

        public CoarseGround(WorldMap map)
        {
            int f = Math.Max(1, (int)Math.Round(64 / map.CellSize));
            (W, H, Cell) = (map.Width / f, map.Height / f, map.CellSize * f);
            var mean = new float[W * H];
            var isWet = new bool[W * H];
            System.Threading.Tasks.Parallel.For(0, H, j =>
            {
                for (int i = 0; i < W; i++)
                {
                    float t = 0f;
                    for (int b = 0; b < f; b++)
                        for (int a = 0; a < f; a++)
                        {
                            int c = (j * f + b) * map.Width + i * f + a;
                            t += map.Elevation[c];
                            if (map.Elevation[c] < 2.5f || map.RiverFlow[c] > 0f && Hydrology.RiverWidth(map, map.RiverFlow[c]) > Culvert) isWet[j * W + i] = true;
                        }
                    mean[j * W + i] = t / (f * f);
                }
            });
            int s = W + 1;
            (sum, sq, wet, steep) = (new double[s * (H + 1)], new double[s * (H + 1)], new int[s * (H + 1)], new int[s * (H + 1)]);
            float rise = TypedCliff * (float)Cell;
            for (int j = 0; j < H; j++)
                for (int i = 0; i < W; i++)
                {
                    int c = j * W + i, o = (j + 1) * s + i + 1;
                    float m = mean[c];
                    bool st = (i + 1 < W && MathF.Abs(mean[c + 1] - m) > rise) || (j + 1 < H && MathF.Abs(mean[c + W] - m) > rise);
                    sum[o] = m + sum[o - 1] + sum[o - s] - sum[o - s - 1];
                    sq[o] = (double)m * m + sq[o - 1] + sq[o - s] - sq[o - s - 1];
                    wet[o] = (isWet[c] ? 1 : 0) + wet[o - 1] + wet[o - s] - wet[o - s - 1];
                    steep[o] = (st ? 1 : 0) + steep[o - 1] + steep[o - s] - steep[o - s - 1];
                }
        }

        /// <summary>The cells under a box in metres (never across a seam): wet and steep ones, and the land's spread about its mean.</summary>
        public (int Wet, int Steep, double Rms) Box(double x0, double z0, double x1, double z1)
        {
            int i0 = Math.Clamp((int)(x0 / Cell), 0, W - 1), j0 = Math.Clamp((int)(z0 / Cell), 0, H - 1);
            int i1 = Math.Clamp((int)Math.Ceiling(x1 / Cell), i0 + 1, W), j1 = Math.Clamp((int)Math.Ceiling(z1 / Cell), j0 + 1, H);
            int s = W + 1, a = j0 * s + i0, b = j0 * s + i1, c = j1 * s + i0, d = j1 * s + i1;
            double n = (i1 - i0) * (j1 - j0), m = (sum[d] - sum[b] - sum[c] + sum[a]) / n;
            return (wet[d] - wet[b] - wet[c] + wet[a], steep[d] - steep[b] - steep[c] + steep[a], Math.Sqrt(Math.Max(0, (sq[d] - sq[b] - sq[c] + sq[a]) / n - m * m)));
        }
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldMap, CoarseGround> coarse = new();

    /// <summary>The widest stream a typed place's made ground is laid over, in a culvert, m; a wider river turns it away.</summary>
    const float Culvert = 12f;

    /// <summary>
    /// Sites a typed place as near where the plan wanted it as good ground allows. One coarse pass over the whole world's
    /// ground (<see cref="CoarseGround"/>) ranks every spot and quarter turn; the best few, apart, are then weighed on the
    /// ground itself, a batch at a time, until one takes it. Another of its types follows where none of the first fits.
    /// </summary>
    static TypedPlace SeatTypeNear(PoiLibrary lib, FamilyDef family, int variant, PoiState state, ulong spawn, WorldProfile profile, PlanetTerrainSource bare, WorldMap map,
        double x, double z, LandingSite landing, List<PoiSite> others)
    {
        // Its type as the world's age and size pick it, then any other of its types that fits the world (an airport with no
        // ground anywhere for it gives way to an airstrip).
        TechLevel? age = profile?.LevelFor(family, state);
        Sketch first = ForceType is { } forced && family.Types.Contains(forced) && lib.Types.FirstOrDefault(t => t.Id == forced) is { } type
            ? PlaceTypes.Plan(type, spawn, map.WidthMeters, map.HeightMeters)
            : PlaceTypes.PlanFor(lib, family, age, spawn, map.WidthMeters, map.HeightMeters, family.Variants.ElementAtOrDefault(variant)?.Port ?? profile?.Starport);
        // (Where no ground takes it as rolled, the same type at its low ends: a real one still, only shorter; then the
        // smaller types after it, never a bigger one.)
        var sketches = family.Types.SkipWhile(t => t != first.Type.Id).Skip(1).Select(t => PlaceTypes.Plan(lib.Types.First(x => x.Id == t), spawn, map.WidthMeters, map.HeightMeters)).Where(s => !s.TooBig)
            .Prepend(first.Fitted > 0f ? PlaceTypes.Plan(first.Type, spawn, map.WidthMeters, map.HeightMeters, 0f) : null).Where(s => s != null).Prepend(first);
        foreach (Sketch sketch in sketches)
        {
            if (SeatType(lib, family, variant, state, spawn, profile, bare, map, x, z, landing, others, sketch) is { } typed) return typed;
            Godot.GD.Print($"{family.Id}: no ground anywhere for its {sketch.Type.Id} ({sketch.Footprint.W:F0} x {sketch.Footprint.D:F0} m): {LastTypedRejects}");
        }
        return null;
    }

    /// <summary>Why a spot was turned down, or <see cref="Reject.None"/>.</summary>
    enum Reject { None, Seam, Crowd, Wet, Cliff, Deep }

    /// <summary>
    /// A family planned as a type: its sketch rolled for this spawn and world, then sited by rule. Every spot and quarter
    /// turn is weighed across its whole footprint: dry everywhere, no cliff across it, the least cut and fill, near where it
    /// was wanted, the gate turned toward the landing country where the roads come from. The place is graded to one level
    /// with an easing out to the land as long as the cut asks for; its parts are set down as places of their own with their
    /// layouts left until wanted. Null when no spot on the world will take it.
    /// </summary>
    static TypedPlace SeatType(PoiLibrary lib, FamilyDef family, int variant, PoiState state, ulong spawn, WorldProfile profile, PlanetTerrainSource bare, WorldMap map,
        double wantX, double wantZ, LandingSite landing, List<PoiSite> others, Sketch sketch)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double width = map.WidthMeters, height = map.HeightMeters;
        TechLevel? age = profile?.LevelFor(family, state);
        CoarseGround ground = coarse.GetValue(map, m => new CoarseGround(m));
        double coarseMs = clock.Elapsed.TotalMilliseconds;
        // The coarse pass: every spot on the world at a step of a third of the place's short side (120 m at least), four
        // turns each.
        double step = ground.Cell * Math.Max(1, Math.Round(Math.Max(120f, sketch.Footprint.Short / 3f) / ground.Cell));
        int nx = (int)(width / step), nz = (int)(height / step);
        var rank = new (double Score, double X, double Z, int Turn)[nx * nz * 4];
        var counts = new int[6];
        System.Threading.Tasks.Parallel.For(0, rank.Length, idx =>
        {
            rank[idx].Score = double.MaxValue;
            int turn = idx % 4, cell = idx / 4;
            double cx = (cell % nx + 0.5) * step, cz = (cell / nx + 0.5) * step;
            Reject why = Rough(sketch, turn, cx, cz, width, height, landing, ground, out double rms);
            if (why != Reject.None) { System.Threading.Interlocked.Increment(ref counts[(int)why]); return; }
            // The cut runs to about twice the spread.
            rank[idx] = (rms * 10 + Dist(cx, cz, wantX, wantZ, width, height) * 0.01 + Facing(sketch, turn, cx, cz, landing, width, height) * 20, cx, cz, turn);
        });
        // The fine pass: the best-ranked spots, apart and clear of other places, weighed on the ground itself a batch at a
        // time (taken best first from a heap: only the few weighed are ever put in order).
        var order = new PriorityQueue<(double Score, double X, double Z, int Turn), double>(rank.Length / 2);
        foreach (var r in rank) if (r.Score < double.MaxValue) order.Enqueue(r, r.Score);
        double rankMs = clock.Elapsed.TotalMilliseconds - coarseMs;
        var tried = new List<(double X, double Z)>();
        (double Score, double X, double Z, int Turn, int Pad, float Cut) best = (double.MaxValue, 0, 0, 0, 0, 0);
        for (int batch = 0; batch < 10 && best.Score == double.MaxValue && order.Count > 0; batch++)
        {
            var pick = new List<(double Score, double X, double Z, int Turn)>();
            while (pick.Count < 8 && order.TryDequeue(out var r, out _))
            {
                if (tried.Any(t => Dist(t.X, t.Z, r.X, r.Z, width, height) < step * 1.5)) continue;
                if (Crowded(sketch, r.Turn, r.X, r.Z, others, width, height)) { counts[(int)Reject.Crowd]++; continue; }
                tried.Add((r.X, r.Z));
                pick.Add(r);
            }
            var weighed = new (double Score, int Pad, float Cut, Reject Why)[pick.Count];
            System.Threading.Tasks.Parallel.For(0, pick.Count, k => weighed[k] = Weigh(sketch, pick[k].Turn, pick[k].X, pick[k].Z, width, height, bare));
            for (int k = 0; k < pick.Count; k++)
            {
                if (weighed[k].Why != Reject.None) { counts[(int)weighed[k].Why]++; continue; }
                double score = weighed[k].Score + Dist(pick[k].X, pick[k].Z, wantX, wantZ, width, height) * 0.01 + Facing(sketch, pick[k].Turn, pick[k].X, pick[k].Z, landing, width, height) * 20;
                if (score < best.Score) best = (score, pick[k].X, pick[k].Z, pick[k].Turn, weighed[k].Pad, weighed[k].Cut);
            }
        }
        LastTypedMs += clock.Elapsed.TotalMilliseconds;
        LastTypedRejects = $"{rank.Length} spots, {tried.Count} weighed: {counts[1]} by a seam or the landing, {counts[2]} crowded, {counts[3]} wet, {counts[4]} across a cliff, {counts[5]} too much cut or bank" +
            $" (coarse ground {coarseMs:F0} ms, ranking {rankMs:F0}, weighing {clock.Elapsed.TotalMilliseconds - coarseMs - rankMs:F0})";
        if (best.Score == double.MaxValue) return null;
        clock.Restart();
        // On the 2 m lattice, so its laid ground and its parts' blocks line up.
        (best.X, best.Z) = (Mathx.WrapX(2 * Math.Round(best.X / 2), width), 2 * Math.Round(best.Z / 2));
        var place = new TypedPlace { Sketch = sketch, Turn = best.Turn, X = best.X, Z = best.Z, PadY = best.Pad };
        SketchBox fb = PlaceTypes.Turn(sketch.Footprint, best.Turn);
        // Graded the way a real one is: the whole of it at one level, eased out to the land over as long a run as the
        // ground at its edge asks for (a gentle bank, never a wall or a pit), up to a third of its length and 300 m (Ease).
        // (Never out into a seam's ghost band.)
        double clearX = Math.Min(best.X + fb.X0, width - (best.X + fb.X1)), clearZ = Math.Min(best.Z + fb.Z0, height - (best.Z + fb.Z1));
        float most = (float)Math.Max(20, Math.Min(Ease(fb), Math.Min(clearX, clearZ) - SeamMargin - Apron)), blend = Math.Min(40f, most);
        for (int pass = 0; pass < 8; pass++)
        {
            // The land round its edge, out across the easing: along every side and round the corners.
            float edge = 0f;
            for (int k = 0; k < 96; k++)
                foreach (double f in new[] { 0.35, 0.7, 1.0 })
                {
                    double t = (k % 24) / 23.0, o = Apron + blend * f;
                    var (px, pz) = (k / 24) switch
                    {
                        0 => (fb.X0 + fb.W * t, fb.Z0 - o), 1 => (fb.X0 + fb.W * t, fb.Z1 + o),
                        2 => (fb.X0 - o, fb.Z0 + fb.D * t), _ => (fb.X1 + o, fb.Z0 + fb.D * t),
                    };
                    edge = MathF.Max(edge, MathF.Abs(bare.NaturalHeight(Mathx.WrapX(best.X + px, width), best.Z + pz) - best.Pad));
                }
            float wanted = Math.Clamp(edge * 12f, Math.Min(40f, most), most);
            if (wanted <= blend) break;
            blend = wanted;
        }
        int x0 = (int)Math.Floor(Mathx.WrapX(best.X + fb.X0, width)), z0 = (int)Math.Floor(best.Z + fb.Z0);
        float wear = PoiInstance.AgeFor(family, state, false) switch { PoiGenerator.Gen.Age.New => 0.1f, PoiGenerator.Gen.Age.Preserved => 0.35f, _ => 0.85f };
        // (Two metres past its footprint either way, so the fence's posts at its corners stand on the level too.)
        place.Stamp = new TerrainStamp(family.Name, x0 - 2, z0 - 2, x0 + (int)fb.W + 2, z0 + (int)fb.D + 2, best.Pad, Apron, blend, width, height) { Surface = StampSurface.Natural, Wear = wear, Painted = true };
        place.Why = $"{sketch.Type.Id} {fb.W:F0} x {fb.D:F0} m, turned {best.Turn}, cut {best.Cut:F1} m, graded out over {blend:F0} m";
        // Its parts: each a place of its own at its box, laid out when first wanted, from its own seed.
        foreach (TypePiece piece in PlaceTypes.Pieces(sketch, best.Turn))
        {
            ulong seed = PoiGenerator.Gen.Hash.Combine(spawn, PoiGenerator.Gen.Hash.Of(piece.Plan.Id), (ulong)(piece.OffsetX * 7919 + piece.OffsetZ));
            // (A recessed pad's wall stands on its pit's floor; its pit is cut after the place's grading, which the main
            // building, first of the parts, holds.)
            int sink = (int)PlaceTypes.Sink(piece.Item);
            var origin = new TInt3((int)Math.Floor(Mathx.WrapX(best.X + piece.OffsetX, width)), best.Pad - sink, (int)Math.Floor(best.Z + piece.OffsetZ));
            var (bx0, bz0, bx1, bz1) = place.World(piece.Box);
            int px0 = (int)Math.Floor(Mathx.WrapX(bx0, width)), pz0 = (int)Math.Floor(bz0), pw = (int)piece.Box.W, pd = (int)piece.Box.D;
            int top = best.Pad + (int)Math.Ceiling(Math.Max(piece.Item.H, 4f)) + 12;
            var stamp = new TerrainStamp($"{family.Name} {piece.Item.Part.Id}", px0, pz0, px0 + pw, pz0 + pd, best.Pad, 0f, 0f, width, height) { Surface = StampSurface.Natural, Wear = wear, Painted = true };
            if (sink > 0)
            {
                var (gx, gz) = PlaceTypes.Turn(piece.Item.Rolled["gx"], piece.Item.Rolled["gz"], best.Turn);
                stamp = new TerrainStamp($"{family.Name} {piece.Item.Part.Id}", px0, pz0, px0 + pw, pz0 + pd, best.Pad, 0f, 0f, width, height)
                {
                    Shape = StampShape.Pit, Radius = piece.Item.Rolled["r"], Depth = sink, DirX = gx, DirZ = gz, Length = sink * PlaceTypes.RampRun,
                    Width = float.Parse(piece.Plan.Parts[0].Arg("ramp"), System.Globalization.CultureInfo.InvariantCulture), Surface = StampSurface.Natural, Wear = wear, Painted = true,
                };
            }
            PoiGenerator.Gen.Int3 v = piece.Front.Vec();
            var entrance = new TInt3((int)Math.Floor(Mathx.WrapX((bx0 + bx1) / 2 + v.X * ((bx1 - bx0) / 2 + 3), width)), best.Pad, (int)Math.Floor((bz0 + bz1) / 2 + v.Z * ((bz1 - bz0) / 2 + 3)));
            SiteDef plan = piece.Plan;
            Dir front = piece.Front;
            var site = new PoiSite
            {
                Name = family.Name, Family = family, State = state, Origin = origin, Stamp = stamp, Whole = place, Siting = "part", Entrance = entrance,
                Footprint = new CellBox(new TInt3(px0 - 4, best.Pad - sink - 2, pz0 - 4), new TInt3(px0 + pw + 4, top, pz0 + pd + 4)),
                Gates = new List<PoiGate> { new("entrance", entrance, v.X, v.Z) }, Recarve = () => { },
                Lay = () => PoiFactory.BuildLayout(lib, family, variant, family.Rarity, seed, new LayoutHints(front, age, Ruin: profile?.Ruin, Site: plan)),
            };
            site.Make = () => PoiFactory.Spawn(lib, site.Layout, state, seed, profile);
            place.Parts.Add(site);
            if (piece.Item.Part.Id == "terminal") place.Anchor ??= site;
        }
        // The world's road comes to the gate, where the access road meets the fence: the terminal holds it, and the stamp.
        place.Anchor ??= place.Parts[0];
        {
            var (gx, gz) = PlaceTypes.Turn(sketch.GateX, sketch.GateZ, best.Turn);
            var (ox, oz) = PlaceTypes.Turn(sketch.OutX, sketch.OutZ, best.Turn);
            var gate = new TInt3((int)Math.Floor(Mathx.WrapX(best.X + gx + ox * 3, width)), best.Pad, (int)Math.Floor(best.Z + gz + oz * 3));
            place.Anchor.Gates = new List<PoiGate> { new("gate", gate, (int)ox, (int)oz) };
            place.Anchor.Siting = "open";
            place.Anchor.Extra.Add(place.Stamp);
        }
        LastTyped++;
        LastTypedMs += clock.Elapsed.TotalMilliseconds;
        return place;
    }

    /// <summary>The coarse look at a spot: off the seams and the landing, its core dry, free of cliffs and not too uneven on the coarse ground.</summary>
    static Reject Rough(Sketch sketch, int turn, double cx, double cz, double width, double height, LandingSite landing, CoarseGround ground, out double rms)
    {
        rms = 0;
        SketchBox b = PlaceTypes.Turn(sketch.Footprint, turn), core = PlaceTypes.Turn(sketch.Core, turn);
        double bx0 = cx + b.X0, bz0 = cz + b.Z0, bx1 = cx + b.X1, bz1 = cz + b.Z1;
        if (bx0 < SeamMargin || bx1 > width - SeamMargin || bz0 < SeamMargin || bz1 > height - SeamMargin
            || Dist((bx0 + bx1) / 2, (bz0 + bz1) / 2, landing.X, landing.Y, width, height) < b.Long / 2 + 200) return Reject.Seam;
        (int wet, int steep, rms) = ground.Box(cx + core.X0, cz + core.Z0, cx + core.X1, cz + core.Z1);
        return wet > 0 ? Reject.Wet : steep > 0 ? Reject.Cliff : rms * 2 > TypedCutFill ? Reject.Deep : Reject.None;
    }

    /// <summary>The longest a typed place's grading eases out to the land, m: a third of its length, 120 to 300.</summary>
    static float Ease(SketchBox b) => Math.Clamp(b.Long / 3f, 120f, 300f);

    /// <summary>Whether a spot, with the bank it may be graded out over, runs into another place's ground.</summary>
    static bool Crowded(Sketch sketch, int turn, double cx, double cz, List<PoiSite> others, double width, double height)
    {
        SketchBox b = PlaceTypes.Turn(sketch.Footprint, turn);
        float ease = Ease(b);
        double mx = cx + (b.X0 + b.X1) / 2, mz = cz + (b.Z0 + b.Z1) / 2;
        foreach (PoiSite o in others)
        {
            TerrainStamp t = o.Whole?.Stamp ?? o.Stamp;
            if (Math.Abs(Mathx.WrapDelta(o.Center.X - mx, width)) < t.ReachX + b.W / 2 + ease + 60
                && Math.Abs(Mathx.WrapDelta(o.Center.Z - mz, height)) < t.ReachZ + b.D / 2 + ease + 60) return true;
        }
        return false;
    }

    /// <summary>
    /// A spot weighed on the ground itself: what must stand dry and even (the lights and the fence may cross a stream),
    /// every 75 m or so (the coarse pass has looked at every 64 m), with no sea, no river too wide for a culvert and no cliff, and the rest of it at least off the
    /// sea; then its cut and spread.
    /// </summary>
    static (double Score, int Pad, float Cut, Reject Why) Weigh(Sketch sketch, int turn, double cx, double cz, double width, double height, PlanetTerrainSource bare)
    {
        SketchBox b = PlaceTypes.Turn(sketch.Footprint, turn), core = PlaceTypes.Turn(sketch.Core, turn);
        double bx0 = cx + b.X0, bz0 = cz + b.Z0, bx1 = cx + b.X1, bz1 = cz + b.Z1, cx0 = cx + core.X0, cz0 = cz + core.Z0;
        int nx = Math.Clamp((int)(core.W / 75) + 1, 3, 48), nz = Math.Clamp((int)(core.D / 75) + 1, 3, 20);
        var h = new float[nx * nz];
        for (int k = 0; k < nz; k++)
            for (int i = 0; i < nx; i++)
            {
                double sx = Mathx.WrapX(cx0 + core.W * i / (nx - 1), width), sz = cz0 + core.D * k / (nz - 1);
                GroundSample g = bare.Sampler.Sample(sx, sz, 2);
                if (g.Height < 2.5f || g.InRiver && Wide(bare.Map, sx, sz)) return (0, 0, 0, Reject.Wet);
                h[k * nx + i] = g.Height;
            }
        for (int i = 0; i <= 8; i++)
            if (bare.Sampler.Sample(Mathx.WrapX(bx0 + (bx1 - bx0) * (i % 3) / 2, width), bz0 + (bz1 - bz0) * (i / 3) / 2, 2).Height < 2.5f) return (0, 0, 0, Reject.Wet);
        float dx = (float)(core.W / (nx - 1)), dz = (float)(core.D / (nz - 1)), worst = 0f;
        for (int k = 0; k < nz; k++)
            for (int i = 0; i < nx; i++)
            {
                if (i > 0) worst = MathF.Max(worst, MathF.Abs(h[k * nx + i] - h[k * nx + i - 1]) / dx);
                if (k > 0) worst = MathF.Max(worst, MathF.Abs(h[k * nx + i] - h[(k - 1) * nx + i]) / dz);
            }
        if (worst > TypedCliff) return (0, 0, 0, Reject.Cliff);
        var sorted = (float[])h.Clone();
        Array.Sort(sorted);
        int pad = (int)MathF.Round(sorted[sorted.Length / 2]);
        float cut = MathF.Max(sorted[^1] - pad, pad - sorted[0]);
        if (cut > TypedCutFill) return (0, 0, 0, Reject.Deep);
        float ease = Ease(b);
        // The land it is graded out to, a third, two thirds and all the way across the easing: a bank no steeper than one
        // you could drive up (the easing's steepest is half again its mean).
        float bank = 0f;
        for (int k = 0; k < 96; k++)
        {
            double t = k % 8 / 7.0, o = ease * (k / 32 + 1) / 3.0;
            var (px, pz) = (k / 8 % 4) switch
            {
                0 => (bx0 + (bx1 - bx0) * t, bz0 - o), 1 => (bx0 + (bx1 - bx0) * t, bz1 + o),
                2 => (bx0 - o, bz0 + (bz1 - bz0) * t), _ => (bx1 + o, bz0 + (bz1 - bz0) * t),
            };
            bank = MathF.Max(bank, MathF.Abs(bare.Sampler.Sample(Mathx.WrapX(px, width), Math.Clamp(pz, 0, height), 2).Height - pad));
        }
        if (bank * 1.5f / ease > TypedBank) return (0, 0, 0, Reject.Deep);
        // Room to grade it out, cut and bank alike, before a seam's ghost band.
        if (Math.Min(Math.Min(bx0, width - bx1), Math.Min(bz0, height - bz1)) - SeamMargin < Math.Min(MathF.Max(cut, bank) * 12f + 20f, ease)) return (0, 0, 0, Reject.Seam);
        return (cut * 3 + bank + Math.Sqrt(h.Average(v => (v - pad) * (v - pad))) * 4, pad, cut, Reject.None);
    }

    /// <summary>Whether a river at a point is too wide to be laid over in a culvert (the widest round its map cell).</summary>
    static bool Wide(WorldMap map, double x, double z)
    {
        int i = (int)Math.Floor(x / map.CellSize), j = (int)Math.Floor(z / map.CellSize);
        for (int b = -1; b <= 1; b++)
            for (int a = -1; a <= 1; a++)
                if (map.RiverFlow[map.Index(i + a, j + b)] is > 0f and var flow && Hydrology.RiverWidth(map, flow) > Culvert) return true;
        return false;
    }

    /// <summary>How far the gate turns from the landing country, where the world's roads come from: 0 straight at it, 2 away.</summary>
    static double Facing(Sketch sketch, int turn, double cx, double cz, LandingSite landing, double width, double height)
    {
        var (gx, gz) = PlaceTypes.Turn(sketch.GateX, sketch.GateZ, turn);
        var (ox, oz) = PlaceTypes.Turn(sketch.OutX, sketch.OutZ, turn);
        double lx = Mathx.WrapDelta(landing.X - (cx + gx), width), lz = Mathx.WrapDelta(landing.Y - (cz + gz), height);
        return 1 - (lx * ox + lz * oz) / Math.Max(1, Math.Sqrt(lx * lx + lz * lz));
    }
}
