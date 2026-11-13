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
    /// Its laid ground as the terrain shader paints it: the runway over its shoulders with its markings, the taxiways
    /// and links with theirs, the apron in slabs, the car park in bays, the access road; worn by the place's age.
    /// </summary>
    public IEnumerable<GroundPatch> Grounds(float wear)
    {
        foreach (var (item, box) in PlaceTypes.Grounds(Sketch, Turn))
        {
            var (x0, z0, x1, z1) = World(box);
            MadeCover cover = item.Part.What switch { "tarmac" => MadeCover.Tarmac, "concrete" => MadeCover.Concrete, "gravel" => MadeCover.Gravel, _ => MadeCover.Dirt };
            float shoulder = item.Rolled.GetValueOrDefault("shoulder");
            GroundPattern pattern = item.Part.Id switch
            {
                "runway" => cover is MadeCover.Tarmac or MadeCover.Concrete ? GroundPattern.Runway : GroundPattern.Plain,
                "taxiway" or "link" => cover is MadeCover.Tarmac or MadeCover.Concrete ? GroundPattern.Taxiway : GroundPattern.Plain,
                "carpark" => cover is MadeCover.Tarmac or MadeCover.Concrete ? GroundPattern.Bays : GroundPattern.Plain,
                _ => cover == MadeCover.Concrete ? GroundPattern.Slabs : GroundPattern.Plain,
            };
            int layer = item.Part.Id switch { "runway" => 4, "taxiway" => 3, "link" => 2, _ => 1 };
            if (shoulder > 0f)
            {
                // The shoulders: the full width, older-looking and plain, under the runway proper.
                yield return GroundPatch.Box(x0, z0, x1, z1, PadY, cover == MadeCover.Dirt ? MadeCover.Dirt : MadeCover.Gravel, GroundPattern.Plain, Math.Min(1f, wear + 0.15f), layer - 1, round: 0.5f);
                bool alongX = x1 - x0 >= z1 - z0;
                if (alongX) (z0, z1) = (z0 + shoulder, z1 - shoulder); else (x0, x1) = (x0 + shoulder, x1 - shoulder);
            }
            var patch = GroundPatch.Box(x0, z0, x1, z1, PadY, cover, pattern, wear, layer, round: 0.5f);
            if (cover == MadeCover.Concrete) patch.Slab = 7.5f;
            yield return patch;
        }
    }
}

public static partial class PoiSiting
{
    /// <summary>The steepest a typed place's ground may run between two samples, rise over run (a cliff across it rules a spot out).</summary>
    const float TypedCliff = 0.3f;
    /// <summary>The most a typed place may cut or fill, m.</summary>
    const float TypedCutFill = 30f;

    /// <summary>Tests: what the last world's typed places cost to site, ms, and how many there were.</summary>
    public static double LastTypedMs;
    public static int LastTyped;
    /// <summary>Tests and screenshots: the type a typed family is planned as, whatever its world's age.</summary>
    public static string ForceType;
    /// <summary>Tests: why the last typed place's spots were turned down.</summary>
    public static string LastTypedRejects = "";

    /// <summary>Sites a typed place where the plan wanted it, or failing that the next good ground out round it (rings a search wide apart, up to <paramref name="rings"/>).</summary>
    static TypedPlace SeatTypeNear(PoiLibrary lib, FamilyDef family, int variant, PoiState state, ulong spawn, WorldProfile profile, PlanetTerrainSource bare, WorldMap map,
        double x, double z, LandingSite landing, List<PoiSite> others, int rings)
    {
        // Its type as the world's age and size pick it, then any other of its types that fits the world (an airport with no
        // ground anywhere for it gives way to an airstrip).
        TechLevel? age = profile?.LevelFor(family, state);
        Sketch first = ForceType is { } forced && lib.Types.FirstOrDefault(t => t.Id == forced) is { } type
            ? PlaceTypes.Plan(type, spawn, map.WidthMeters, map.HeightMeters)
            : PlaceTypes.PlanFor(lib, family, age, spawn, map.WidthMeters, map.HeightMeters);
        var sketches = family.Types.Where(t => t != first.Type.Id).Select(t => PlaceTypes.Plan(lib.Types.First(x => x.Id == t), spawn, map.WidthMeters, map.HeightMeters)).Where(s => !s.TooBig).Prepend(first);
        foreach (Sketch sketch in sketches)
        {
            TypedPlace typed = SeatType(lib, family, variant, state, spawn, profile, bare, map, x, z, landing, others, sketch);
            for (int k = 0; typed == null && k < rings * 3; k++)
            {
                double a = k * 2.399963, r = 1600 * (1 + k / 3);
                typed = SeatType(lib, family, variant, state, spawn, profile, bare, map, Mathx.WrapX(x + Math.Cos(a) * r, map.WidthMeters), Mathx.Wrap(z + Math.Sin(a) * r, map.HeightMeters), landing, others, sketch);
            }
            // Last, the whole world at a coarse step, the nearest good ground winning.
            typed ??= SeatType(lib, family, variant, state, spawn, profile, bare, map, x, z, landing, others, sketch, Math.Max(map.WidthMeters, map.HeightMeters) / 2);
            if (typed != null) return typed;
            Godot.GD.Print($"{family.Id}: no ground anywhere for its {sketch.Type.Id} ({sketch.Footprint.W:F0} x {sketch.Footprint.D:F0} m); the world-wide search: {LastTypedRejects}");
        }
        return null;
    }

    /// <summary>
    /// A family planned as a type: its sketch rolled for this spawn and world, then sited by rule within reach of where the
    /// plan wanted it. Every spot and quarter turn is weighed on the ground across its whole footprint: dry everywhere, no
    /// cliff across it, the least cut and fill, the gate turned toward the landing country where the roads come from. The
    /// place is graded to one level with an easing out to the land as long as the cut asks for; its parts are set down as
    /// places of their own with their layouts left until wanted. Null when no spot within reach will take it.
    /// </summary>
    static TypedPlace SeatType(PoiLibrary lib, FamilyDef family, int variant, PoiState state, ulong spawn, WorldProfile profile, PlanetTerrainSource bare, WorldMap map,
        double wantX, double wantZ, LandingSite landing, List<PoiSite> others, Sketch sketch, double wide = 0)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double width = map.WidthMeters, height = map.HeightMeters;
        TechLevel? age = profile?.LevelFor(family, state);
        float reach = Math.Max(600f, sketch.Footprint.Long * 0.5f), step = Math.Max(60f, sketch.Footprint.Short / 4f);
        if (wide > 0) (reach, step) = ((float)wide, Math.Max(step, (float)wide / 20f));
        int span = (int)(reach / step) * 2 + 1;
        var close = others.Where(o => Dist(wantX, wantZ, o.Center.X, o.Center.Z, width, height) < reach + sketch.Footprint.Long + 800).ToList();
        var found = new (double Score, double X, double Z, int Turn, int Pad, float Cut)[span * span * 4];
        int seam = 0, crowd = 0, wet = 0, cliff = 0, deep = 0;
        System.Threading.Tasks.Parallel.For(0, found.Length, idx =>
        {
            found[idx].Score = double.MaxValue;
            int turn = idx % 4, cell = idx / 4;
            double cx = Mathx.WrapX(wantX + (cell % span - span / 2) * step, width), cz = Mathx.Wrap(wantZ + (cell / span - span / 2) * step, height);
            SketchBox b = PlaceTypes.Turn(sketch.Footprint, turn);
            double bx0 = cx + b.X0, bz0 = cz + b.Z0, bx1 = cx + b.X1, bz1 = cz + b.Z1;
            if (bx0 < SeamMargin || bx1 > width - SeamMargin || bz0 < SeamMargin || bz1 > height - SeamMargin
                || Dist((bx0 + bx1) / 2, (bz0 + bz1) / 2, landing.X, landing.Y, width, height) < b.Long / 2 + 200) { System.Threading.Interlocked.Increment(ref seam); return; }
            foreach (PoiSite o in close)
            {
                TerrainStamp t = o.Whole?.Stamp ?? o.Stamp;
                if (Math.Abs(Mathx.WrapDelta(o.Center.X - (bx0 + bx1) / 2, width)) < t.ReachX + b.W / 2 + 60
                    && Math.Abs(Mathx.WrapDelta(o.Center.Z - (bz0 + bz1) / 2, height)) < t.ReachZ + b.D / 2 + 60) { System.Threading.Interlocked.Increment(ref crowd); return; }
            }
            // The ground across what must stand dry and even (the lights and the fence may cross a stream), every 50 m
            // or so: no water, no cliff, its spread; the rest of it at least off the sea.
            SketchBox core = PlaceTypes.Turn(sketch.Core, turn);
            double cx0 = cx + core.X0, cz0 = cz + core.Z0;
            int nx = Math.Clamp((int)(core.W / 50) + 1, 3, 60), nz = Math.Clamp((int)(core.D / 50) + 1, 3, 30);
            var h = new float[nx * nz];
            for (int k = 0; k < nz; k++)
                for (int i = 0; i < nx; i++)
                {
                    GroundSample g = bare.Sampler.Sample(Mathx.WrapX(cx0 + core.W * i / (nx - 1), width), cz0 + core.D * k / (nz - 1), 2);
                    if (g.InRiver || g.Height < 2.5f) { System.Threading.Interlocked.Increment(ref wet); return; }
                    h[k * nx + i] = g.Height;
                }
            for (int i = 0; i <= 8; i++)
                if (bare.Sampler.Sample(Mathx.WrapX(bx0 + (bx1 - bx0) * (i % 3) / 2, width), bz0 + (bz1 - bz0) * (i / 3) / 2, 2).Height < 2.5f) { System.Threading.Interlocked.Increment(ref wet); return; }
            float dx = (float)(core.W / (nx - 1)), dz = (float)(core.D / (nz - 1)), worst = 0f;
            for (int k = 0; k < nz; k++)
                for (int i = 0; i < nx; i++)
                {
                    if (i > 0) worst = MathF.Max(worst, MathF.Abs(h[k * nx + i] - h[k * nx + i - 1]) / dx);
                    if (k > 0) worst = MathF.Max(worst, MathF.Abs(h[k * nx + i] - h[(k - 1) * nx + i]) / dz);
                }
            if (worst > TypedCliff) { System.Threading.Interlocked.Increment(ref cliff); return; }
            var sorted = (float[])h.Clone();
            Array.Sort(sorted);
            int pad = (int)MathF.Round(sorted[sorted.Length / 2]);
            float cut = MathF.Max(sorted[^1] - pad, pad - sorted[0]);
            if (cut > TypedCutFill) { System.Threading.Interlocked.Increment(ref deep); return; }
            // Room to grade it out before a seam's ghost band.
            if (Math.Min(Math.Min(bx0, width - bx1), Math.Min(bz0, height - bz1)) - SeamMargin < Math.Min(cut * 12f + 20f, b.Long / 3f)) { System.Threading.Interlocked.Increment(ref seam); return; }
            double rms = Math.Sqrt(h.Average(v => (v - pad) * (v - pad)));
            // The gate turned toward the landing country, where the world's roads come from.
            var (gx, gz) = PlaceTypes.Turn(sketch.GateX, sketch.GateZ, turn);
            var (ox, oz) = PlaceTypes.Turn(sketch.OutX, sketch.OutZ, turn);
            double lx = Mathx.WrapDelta(landing.X - (cx + gx), width), lz = Mathx.WrapDelta(landing.Y - (cz + gz), height), ll = Math.Max(1, Math.Sqrt(lx * lx + lz * lz));
            double facing = 1 - (lx * ox + lz * oz) / ll;
            found[idx] = (cut * 3 + rms * 4 + Dist(cx, cz, wantX, wantZ, width, height) * 0.01 + facing * 20, cx, cz, turn, pad, cut);
        });
        var ok = found.Where(f => f.Score < double.MaxValue).ToList();
        LastTypedMs += clock.Elapsed.TotalMilliseconds;
        LastTypedRejects = $"{found.Length} spots: {seam} by a seam or the landing, {crowd} crowded, {wet} wet, {cliff} across a cliff, {deep} too much cut";
        if (ok.Count == 0) return null;
        clock.Restart();
        var best = ok.MinBy(f => f.Score);
        // On the 2 m lattice, so its laid ground and its parts' blocks line up.
        (best.X, best.Z) = (Mathx.WrapX(2 * Math.Round(best.X / 2), width), 2 * Math.Round(best.Z / 2));
        var place = new TypedPlace { Sketch = sketch, Turn = best.Turn, X = best.X, Z = best.Z, PadY = best.Pad };
        SketchBox fb = PlaceTypes.Turn(sketch.Footprint, best.Turn);
        // Graded the way a real one is: the whole of it at one level, eased out to the land over as long a run as the
        // ground at its edge asks for (a gentle bank, never a wall or a pit), up to a third of its length.
        // (Never out into a seam's ghost band.)
        double clearX = Math.Min(best.X + fb.X0, width - (best.X + fb.X1)), clearZ = Math.Min(best.Z + fb.Z0, height - (best.Z + fb.Z1));
        float most = (float)Math.Max(20, Math.Min(Math.Max(120f, fb.Long / 3f), Math.Min(clearX, clearZ) - SeamMargin - Apron)), blend = Math.Min(40f, most);
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
        place.Stamp = new TerrainStamp(family.Name, x0, z0, x0 + (int)fb.W, z0 + (int)fb.D, best.Pad, Apron, blend, width, height) { Surface = StampSurface.Natural, Wear = wear, Painted = true };
        place.Why = $"{sketch.Type.Id} {fb.W:F0} x {fb.D:F0} m, turned {best.Turn}, cut {best.Cut:F1} m, graded out over {blend:F0} m";
        // Its parts: each a place of its own at its box, laid out when first wanted, from its own seed.
        foreach (TypePiece piece in PlaceTypes.Pieces(sketch, best.Turn))
        {
            ulong seed = PoiGenerator.Gen.Hash.Combine(spawn, PoiGenerator.Gen.Hash.Of(piece.Plan.Id), (ulong)(piece.OffsetX * 7919 + piece.OffsetZ));
            var origin = new TInt3((int)Math.Floor(Mathx.WrapX(best.X + piece.OffsetX, width)), best.Pad, (int)Math.Floor(best.Z + piece.OffsetZ));
            var (bx0, bz0, bx1, bz1) = place.World(piece.Box);
            int px0 = (int)Math.Floor(Mathx.WrapX(bx0, width)), pz0 = (int)Math.Floor(bz0), pw = (int)piece.Box.W, pd = (int)piece.Box.D;
            int top = best.Pad + (int)Math.Ceiling(Math.Max(piece.Item.H, 4f)) + 12;
            var stamp = new TerrainStamp($"{family.Name} {piece.Item.Part.Id}", px0, pz0, px0 + pw, pz0 + pd, best.Pad, 0f, 0f, width, height) { Surface = StampSurface.Natural, Wear = wear, Painted = true };
            PoiGenerator.Gen.Int3 v = piece.Front.Vec();
            var entrance = new TInt3((int)Math.Floor(Mathx.WrapX((bx0 + bx1) / 2 + v.X * ((bx1 - bx0) / 2 + 3), width)), best.Pad, (int)Math.Floor((bz0 + bz1) / 2 + v.Z * ((bz1 - bz0) / 2 + 3)));
            SiteDef plan = piece.Plan;
            Dir front = piece.Front;
            var site = new PoiSite
            {
                Name = family.Name, Family = family, State = state, Origin = origin, Stamp = stamp, Whole = place, Siting = "part", Entrance = entrance,
                Footprint = new CellBox(new TInt3(px0 - 4, best.Pad - 2, pz0 - 4), new TInt3(px0 + pw + 4, top, pz0 + pd + 4)),
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
}
