using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using Archetype = Planets.Gen.Archetype;

namespace PlanetSurface;

/// <summary>
/// Where water meets the land, as it is drawn: river mouths, rivers widening toward them and plain coasts on
/// fresh worlds, each looked at on a half-metre grid as the near ground (the voxels' surface) and every far
/// level of detail draws it, with the water each of them draws (the river sheets of the far chunks, the sea
/// plane by its mask, by the water shader's rules).
/// </summary>
public partial class SurfaceTest
{
    /// <summary>A hand's breadth: how far drawn water may stand off the level it has, or over the ground at its edge.</summary>
    const float ShoreTol = 0.1f;

    /// <summary>The sampling grid, m, and a window's half width.</summary>
    const float ShoreCell = 0.5f;
    const int ShoreHalf = 32;

    /// <summary>The levels of detail looked at: -1 the near ground (voxels, with the finest far chunk's water), then the far levels.</summary>
    static readonly int[] ShoreLevels = { -1, 0, 1, 2 };

    /// <summary>One world's ground and water as each level of detail draws them.</summary>
    sealed class ShoreWorld
    {
        public readonly PlanetWorld W;
        public readonly PlanetTerrainSource S;
        public readonly Image Mask;
        readonly Dictionary<(int, int, int), FarJob> _jobs = new();
        readonly Dictionary<(int, int, int), Dictionary<(int, int), int>> _sheets = new();
        public readonly double[] BuildMs = new double[3];
        public readonly int[] Builds = new int[3];

        public ShoreWorld(PlanetWorld w)
        {
            W = w;
            S = w.Source;
            Mask = w.SeaTexture().GetImage();
        }

        FarJob Job(int fx, int fz, int lod)
        {
            if (_jobs.TryGetValue((fx, fz, lod), out FarJob job)) return job;
            job = new FarJob { Fx = fx, Fz = fz, Lod = lod, SpanX = FarBuilder.Span(fx, (int)W.Width), SpanZ = FarBuilder.Span(fz, (int)W.Height) };
            FarBuilder.Build(job, S, W.Palette, new HashSet<long>());
            if (job.Error != null) throw job.Error;
            BuildMs[lod] += job.Milliseconds;
            Builds[lod]++;
            _jobs[(fx, fz, lod)] = job;
            // The sheet's squares by grid square: each is two triangles a b c, b d c.
            var squares = new Dictionary<(int, int), int>();
            float step = FarBuilder.Step(lod);
            if (job.WaterIndices != null)
                for (int t = 0; t + 5 < job.WaterIndices.Length; t += 6)
                {
                    Vector3 a = job.WaterVertices[job.WaterIndices[t]];
                    squares[((int)MathF.Round(a.X / step), (int)MathF.Round(a.Z / step))] = t;
                }
            _sheets[(fx, fz, lod)] = squares;
            return job;
        }

        FarJob At(int lod, double x, double z, out float u, out float v)
        {
            x = Mathx.WrapX(x, W.Width);
            z = Mathx.Wrap(z, W.Height);
            int fx = (int)(x / FarBuilder.Size), fz = (int)(z / FarBuilder.Size);
            u = (float)(x - fx * (double)FarBuilder.Size);
            v = (float)(z - fz * (double)FarBuilder.Size);
            return Job(fx, fz, lod);
        }

        /// <summary>The ground drawn at a point: the voxels' surface (level -1) or a far level's mesh.</summary>
        public float Ground(int level, double x, double z)
        {
            if (level < 0) return S.SurfaceHeight(Mathx.WrapX(x, W.Width), Mathx.Wrap(z, W.Height));
            FarJob job = At(level, x, z, out float u, out float v);
            float step = FarBuilder.Step(level);
            int nx = (int)(job.SpanX / step) + 1, nz = (int)(job.SpanZ / step) + 1;
            int i = Math.Clamp((int)(u / step), 0, nx - 2), j = Math.Clamp((int)(v / step), 0, nz - 2);
            int b = (j * (nx - 1) + i) * 6;
            float best = float.MinValue, y = 0f;
            for (int t = 0; t < 2; t++)
            {
                Vector3 p = job.Vertices[job.Indices[b + 3 * t]], q = job.Vertices[job.Indices[b + 3 * t + 1]], r = job.Vertices[job.Indices[b + 3 * t + 2]];
                float det = (q.X - p.X) * (r.Z - p.Z) - (r.X - p.X) * (q.Z - p.Z);
                if (MathF.Abs(det) < 1e-9f) continue;
                float l1 = ((u - p.X) * (r.Z - p.Z) - (r.X - p.X) * (v - p.Z)) / det;
                float l2 = ((q.X - p.X) * (v - p.Z) - (u - p.X) * (q.Z - p.Z)) / det;
                float l0 = 1f - l1 - l2, least = MathF.Min(l0, MathF.Min(l1, l2));
                if (least <= best) continue;
                best = least;
                y = p.Y * l0 + q.Y * l1 + r.Y * l2;
            }
            return y;
        }

        /// <summary>A far level's river sheet at a point, if it has a square there: its height.</summary>
        public bool Sheet(int lod, double x, double z, out float y)
        {
            FarJob job = At(lod, x, z, out float u, out float v);
            float step = FarBuilder.Step(lod);
            y = 0f;
            int i = (int)MathF.Floor(u / step), j = (int)MathF.Floor(v / step);
            if (!_sheets[(job.Fx, job.Fz, lod)].TryGetValue((i, j), out int t)) return false;
            int a = job.WaterIndices[t], b = job.WaterIndices[t + 1], c = job.WaterIndices[t + 2], d = job.WaterIndices[t + 4];
            float fu = u / step - i, fv = v / step - j;
            y = fu + fv <= 1f
                ? job.WaterVertices[a].Y * (1f - fu - fv) + job.WaterVertices[b].Y * fu + job.WaterVertices[c].Y * fv
                : job.WaterVertices[d].Y * (fu + fv - 1f) + job.WaterVertices[c].Y * (1f - fu) + job.WaterVertices[b].Y * (1f - fv);
            return true;
        }

        /// <summary>What the water shader reads from the sea mask at a point: metres below sea level, as its filter blends the map's cells.</summary>
        public float SeaMask(double x, double z) => PlanetWorld.SeaMaskAt(Mask, Mathx.WrapX(x, W.Width), Mathx.Wrap(z, W.Height), W.Width, W.Height);
    }

    // ---- What is drawn: the water shader's rules ---------------------------------------------------------

    /// <summary>The sea plane draws where its mask says sea.</summary>
    static bool SeaDrawn(ShoreWorld sw, double x, double z) => sw.SeaMask(x, z) > WorldMap.SeaFrom;

    /// <summary>The top of the water drawn at a point (NaN: none), and which: 1 a river sheet, 2 the sea. A river at the sea's level where the sea draws is left to it.</summary>
    static float DrawnWater(ShoreWorld sw, int level, double x, double z, out int kind)
    {
        bool sea = SeaDrawn(sw, x, z);
        kind = sea ? 2 : 0;
        float top = sea ? 0f : float.NaN;
        if (sw.Sheet(Math.Max(level, 0), x, z, out float y) && !(sea && y < 0.05f))
        {
            kind = 1;
            top = y;
        }
        return top;
    }

    /// <summary>The level the water volume stands at over a point, whatever the ground (NaN: no water here).</summary>
    static float WaterLevel(ShoreWorld sw, double x, double z)
    {
        GroundSample g = sw.S.Ground(Mathx.WrapX(x, sw.W.Width), Mathx.Wrap(z, sw.W.Height), 1);
        if (g.InRiver) return g.RiverLevel - SurfaceSampler.RiverDrop;
        return SeaDrawn(sw, x, z) ? 0f : float.NaN;
    }

    // ---- The checks -------------------------------------------------------------------------------------

    sealed class ShoreTally
    {
        public long Points, Wet, Cut, Torn, Floating, Through, Stepped;
        public double Length;          // shoreline drawn, m
        public int Sharp, Straight, Off, Looked;
        public readonly List<float> Apart = new();    // far levels: how far each near shoreline point is from this level's
        readonly Dictionary<string, List<string>> _notes = new();   // the first few points that fail, by what failed

        public void Note(string kind, string what)
        {
            if (!_notes.TryGetValue(kind, out var list)) _notes[kind] = list = new List<string>();
            if (list.Count < 2) list.Add(what);
        }

        /// <summary>The first points noted of these kinds, to end a check's line with.</summary>
        public string Notes(params string[] kinds) => string.Concat(kinds.Where(_notes.ContainsKey).SelectMany(k => _notes[k]).Select(n => "; " + n));
    }

    async Task ShoreChecks()
    {
        var rand = new Random();
        ulong? fixedSeed = null;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--shoreseed=")) fixedSeed = ulong.Parse(arg["--shoreseed=".Length..]);
        var tallies = ShoreLevels.ToDictionary(l => l, _ => new ShoreTally());
        int worlds = 0, mouths = 0, ringed = 0, ringLooked = 0, mouthSteps = 0, edgeLooked = 0, edgeLifted = 0;
        float worstRing = 0f, worstMouthStep = 0f, edgeLow = float.MaxValue;
        var notes = new List<string>();
        double[] buildMs = new double[3];
        int[] builds = new int[3];
        for (int n = 0; n < (fixedSeed == null ? 3 : 1); n++)
        {
            var spec = PlanetSpec.Roll(fixedSeed ?? (ulong)rand.NextInt64(), Archetype.Temperate, rand.Next(2));
            PlanetWorld w = await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
            var sw = new ShoreWorld(w);
            worlds++;
            // The land's own height along the sea's drawn edge, away from rivers: where it is under the water the
            // ground there is raised clear of it (PlanetTerrainSource.Banked), and the sea's edge is the mask's.
            var edge = new List<float>();
            for (int tries = 0; tries < 400000 && edge.Count < 2000; tries++)
            {
                double x = rand.NextDouble() * w.Width, z = rand.NextDouble() * w.Height;
                if (MathF.Abs(sw.SeaMask(x, z) - WorldMap.SeaFrom) > 0.05f) continue;
                GroundSample gs = w.Source.Sampler.Sample(x, z, 1);
                if (!gs.InRiver && float.IsNegativeInfinity(gs.Bank)) edge.Add(gs.Height);
            }
            edgeLooked += edge.Count;
            edgeLifted += edge.Count(h => h < PlanetTerrainSource.ShoreRise);
            if (edge.Count > 0) edgeLow = MathF.Min(edgeLow, Pct(edge, 0.05f));
            var windows = ShoreSites(w, rand);
            GD.Print($"      {spec}: {windows.Count(s => s.Kind == "mouth")} mouths, {windows.Count(s => s.Kind == "reach")} reaches, {windows.Count(s => s.Kind == "bank")} banks, {windows.Count(s => s.Kind == "coast")} coasts");
            foreach (var (kind, cx, cz, along, _, _, _) in windows)
            {
                if (kind == "mouth") mouths++;
                var near = new List<Vector2>();
                foreach (int level in ShoreLevels)
                {
                    List<Vector2> drawn = ShoreWindow(sw, level, cx, cz, tallies[level], level < 0 ? null : near);
                    if (level < 0) near = drawn;
                }
                if (kind != "mouth") continue;
                // The mouth: no ground the river raised out in the sea, and its water at the sea's level where they meet.
                for (float dz = -ShoreHalf; dz <= ShoreHalf; dz += 1f)
                for (float dx = -ShoreHalf; dx <= ShoreHalf; dx += 1f)
                {
                    double x = cx + dx, z = cz + dz;
                    if (sw.SeaMask(x, z) < 1f) continue;
                    ringLooked++;
                    float g = sw.Ground(-1, x, z), natural = w.Map.Model.Elevation(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height), 1);
                    if (g <= natural + 0.1f || g < -0.6f) continue;
                    ringed++;
                    if (g - natural > worstRing) { worstRing = g - natural; notes.Add($"ground raised {g - natural:F1} m to {g:F1} m out in the sea at {x:F0},{z:F0}"); }
                }
                float step = MathF.Abs(along);
                if (step > ShoreTol) { mouthSteps++; worstMouthStep = MathF.Max(worstMouthStep, step); }
                await Frames(1);
            }
            for (int l = 0; l < 3; l++) { buildMs[l] += sw.BuildMs[l]; builds[l] += sw.Builds[l]; }
        }

        GD.Print($"      far chunk builds: {string.Join(", ", Enumerable.Range(0, 3).Select(l => $"lod {l} {buildMs[l] / Math.Max(1, builds[l]):F1} ms ({builds[l]})"))}; " +
                 $"the land at the sea's drawn edge: {edgeLifted} of {edgeLooked} points under the water and raised clear of it, the lowest 5% from {edgeLow:F2} m");
        string where = notes.Count > 0 ? "; " + string.Join("; ", notes.TakeLast(2)) : "";
        Check(worlds > 0 && mouths >= 2 && ringed == 0, $"river mouths raise no ground out in the sea: {ringed} of {ringLooked} metres of sea round {mouths} mouths on {worlds} worlds stand over the land's own height (worst {worstRing:F1} m){where}");
        Check(mouthSteps == 0 && tallies.Values.All(t => t.Stepped == 0), $"a river meets the sea at its level: {mouthSteps} of {mouths} mouths off it (worst {worstMouthStep:F2} m); {tallies.Values.Sum(t => t.Stepped)} places where river water meets the sea's at another height{string.Concat(tallies.Values.Select(t => t.Notes("step")))}");
        foreach (int level in ShoreLevels)
        {
            ShoreTally t = tallies[level];
            string name = level < 0 ? "near ground" : $"far level {level}";
            string shape = $"{t.Length:F0} m of shoreline, {t.Sharp} turns over {SharpTurn} degrees between samples {ChordOf(level):F0} m apart that the land's own line at the water's level doesn't make there, " +
                           $"{t.Straight} straight runs of {StraightRun} m across sloping land, {t.Off} of {t.Looked} points over {OffLine} m off the land's line";
            string stands = $"{t.Floating} points with water drawn more than {ShoreTol * 100:F0} cm over the land and its level (as much more as the level changes out to a grid square's far corner, on rapids and where reaches meet), {t.Through} where the ground shows through it";
            // Every level: the water's own outline never stands over the land beside it.
            Check(t.Cut == 0, $"{name}: water runs in under its banks: {t.Cut} of {t.Wet} wet points stand more than {ShoreTol * 100:F0} cm over the land at the water's own outline{t.Notes("cut")}" +
                $" ({t.Torn} more where a river's channel is torn against another reach lower down, the volume's own wall{t.Notes("torn")})");
            if (level <= 0)
            {
                // Near the eye (the voxels, and the finest far level round them) the water drawn is the water there is, its edge the
                // land's: a sharp turn the land's own line doesn't make is a step (a stray one in a kilometre is the land's
                // finest detail meeting the sampling, not squares).
                Check(t.Floating == 0 && t.Through == 0, $"{name}: water stands at its own level against the land: of {t.Wet} wet points, {stands}{t.Notes("floating", "through")}");
                Check(t.Sharp <= t.Length / 1000 && t.Straight == 0, $"{name}: shorelines follow the land: {shape} (at most one turn a kilometre){t.Notes("turn", "straight", "off")}");
            }
            if (level < 0) continue;
            // Every far level: its shoreline where the near ground's is, to within its own grid step (its coarser ground
            // stands off the land's by that much, and its edge with it).
            float margin = FarBuilder.Step(level), p95 = t.Apart.Count > 0 ? Pct(t.Apart, 0.95f) : 0f;
            Check(t.Apart.Count > 0 && p95 <= margin, $"{name} draws the shoreline where the near ground does: 95% of {t.Apart.Count} near shoreline points within {p95:F2} m of its own (at most {margin:F0} m, its grid step)" +
                (level > 0 ? $"; {shape}; {stands}" : ""));
        }
    }

    static float Pct(List<float> values, float q)
    {
        var sorted = values.OrderBy(x => x).ToList();
        return sorted[Math.Clamp((int)(q * (sorted.Count - 1)), 0, sorted.Count - 1)];
    }

    /// <summary>A turn this sharp between neighbouring pieces of a shoreline is a step; a run this long within 2 degrees across land sloping 1 in 30 or more is a polygon's edge.</summary>
    const float SharpTurn = 60f, StraightRun = 8f, OffLine = 0.3f;

    /// <summary>How far apart a shoreline's samples are taken for its turns: a metre near, a far level's own grid step past its finest (its triangles' corners are where it may turn).</summary>
    static float ChordOf(int level) => level <= 0 ? 1f : FarBuilder.Step(level);

    /// <summary>A place to look at: its kind, where, the river's level against the sea's there (a mouth), and which way the river runs.</summary>
    internal readonly record struct ShoreSite(string Kind, double X, double Z, float Along, float DirX = 0f, float DirZ = 0f, float Width = 0f);

    /// <summary>
    /// The places to look: where rivers reach the sea (the mouth), a stretch upstream where they widen toward it (a
    /// reach, 120 m back) and a bank further up (400 m), and stretches of plain coast away from any river.
    /// </summary>
    internal static List<ShoreSite> ShoreSites(PlanetWorld w, Random rand, int most = 3)
    {
        var sites = new List<ShoreSite>();
        TerrainModel model = w.Map.Model;
        var order = Enumerable.Range(0, w.Map.Rivers.Count).OrderBy(_ => rand.Next()).ToList();
        foreach (int r in order)
        {
            River river = w.Map.Rivers[r];
            if (sites.Count(s => s.Kind == "mouth") >= most) break;
            if (river.Width[^1] < 6f) continue;
            Vector2 Channel(int k)
            {
                RiverFit.Channel(river, k, model, w.Width, w.Height, out double qx, out double qy);
                return new Vector2((float)Mathx.WrapX(qx, w.Width), (float)Mathx.Wrap(qy, w.Height));
            }
            // The mouth: the first point of the channel where the sea is drawn.
            int mouth = -1;
            for (int k = 0; k < river.Count && mouth < 0; k++)
            {
                Vector2 c = Channel(k);
                if (w.Map.IsSea(c.X, c.Y)) mouth = k;
            }
            if (mouth < 8 || mouth + 1 >= river.Count) continue;
            Vector2 at = Channel(mouth);
            GroundSample g = w.Source.Ground(at.X, at.Y, 1);
            float level = g.InRiver ? g.RiverLevel - SurfaceSampler.RiverDrop : float.NaN;
            ShoreSite Site(string kind, int k, float along)
            {
                Vector2 c = Channel(k), dir = new Vector2(river.X[k + 1] - river.X[k - 1], river.Y[k + 1] - river.Y[k - 1]).Normalized();
                return new ShoreSite(kind, c.X, c.Y, along, dir.X, dir.Y, river.Width[k]);
            }
            sites.Add(Site("mouth", mouth, float.IsNaN(level) ? 0f : level));
            // Upstream, where it widens toward the sea (about 120 m back along the line), and a bank further up (400 m).
            double back = 0;
            bool reach = false;
            for (int k = mouth - 1; k > 0; k--)
            {
                back += Math.Sqrt(Math.Pow(river.X[k + 1] - river.X[k], 2) + Math.Pow(river.Y[k + 1] - river.Y[k], 2));
                if (!reach && back >= 120)
                {
                    reach = true;
                    if (sites.Count(s => s.Kind == "reach") < most) sites.Add(Site("reach", k, 0f));
                }
                if (back < 400) continue;
                if (sites.Count(s => s.Kind == "bank") < most) sites.Add(Site("bank", k, 0f));
                break;
            }
        }
        // Plain coast: where the sea's mask crosses its threshold, 200 m or more from any river's line.
        for (int tries = 0; tries < 4000 && sites.Count(s => s.Kind == "coast") < 2; tries++)
        {
            int k = rand.Next(w.Map.Count);
            if (!w.Map.Has(k, CellFlags.Water)) continue;
            int i = k % w.Map.Width, j = k / w.Map.Width;
            if (w.Map.Has(w.Map.Index(i + 1, j), CellFlags.Water) && w.Map.Has(w.Map.Index(i - 1, j), CellFlags.Water)
                && w.Map.Has(w.Map.Index(i, j + 1), CellFlags.Water) && w.Map.Has(w.Map.Index(i, j - 1), CellFlags.Water)) continue;
            double x = w.Map.CellX(i), z = w.Map.CellY(j);
            bool river = w.Map.Rivers.Any(rv => Enumerable.Range(0, rv.Count).Any(p => Math.Abs(Mathx.WrapDelta((double)rv.X[p] - x, w.Width)) < 200 && Math.Abs(Mathx.WrapDelta((double)rv.Y[p] - z, w.Height)) < 200));
            if (!river) sites.Add(new ShoreSite("coast", x, z, 0f));
        }
        return sites;
    }

    /// <summary>
    /// One window at one level of detail: the ground and the water drawn over a half-metre grid, tallied; returns
    /// its drawn shoreline, sampled every half metre. A far level is measured against the near level's shoreline.
    /// </summary>
    static List<Vector2> ShoreWindow(ShoreWorld sw, int level, double cx, double cz, ShoreTally t, List<Vector2> nearLine)
    {
        int n = (int)(2 * ShoreHalf / ShoreCell) + 1;
        double x0 = cx - ShoreHalf, z0 = cz - ShoreHalf;
        var ground = new float[n * n];
        var drawn = new float[n * n];
        var ideal = new float[n * n];
        var tops = new float[n * n];
        var levels = new float[n * n];
        var kinds = new int[n * n];
        var placed = new bool[n * n];   // a place's own ground (its pad and apron): where it meets a river is the place's siting
        // The land's own surface, under a far level's coarser one: its triangles across a channel's side stand lower than
        // the land by a few centimetres, and a water outline over that is the level's, not water standing off the land.
        var land = level < 0 ? ground : new float[n * n];
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            double x = x0 + i * ShoreCell, z = z0 + j * ShoreCell;
            int k = j * n + i;
            ground[k] = sw.Ground(level, x, z);
            if (level >= 0) land[k] = sw.Ground(-1, x, z);
            tops[k] = DrawnWater(sw, level, x, z, out kinds[k]);
            levels[k] = WaterLevel(sw, x, z);
            placed[k] = sw.S.Built(Mathx.WrapX(x, sw.W.Width), Mathx.Wrap(z, sw.W.Height), out _) > 0f;
        }
        float step = FarBuilder.Step(Math.Max(level, 0));
        var torn = new bool[n * n];
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int k = j * n + i;
            double x = x0 + i * ShoreCell, z = z0 + j * ShoreCell;
            float g = ground[k], top = tops[k], lv = levels[k];
            t.Points++;
            bool shows = !float.IsNaN(top) && top > g;
            if (!shows) kinds[k] = 0;
            else t.Wet++;
            drawn[k] = float.IsNaN(top) ? -1f : top - g;
            ideal[k] = float.IsNaN(lv) ? -1f : lv - g;
            if (placed[k]) continue;
            // A sheet on a grid follows a river's level only at its corners: on a rapid, or where two reaches meet, it may
            // stand off the level here by as much as the level changes out to its square's far corner (3 m at most).
            // Worked out only where the water drawn stands off its level at all.
            float tol = ShoreTol;
            if (!float.IsNaN(lv) && (shows && top > lv + tol || !shows && lv > g + tol))
            {
                int reach = Math.Min((int)MathF.Ceiling(step * 1.42f / ShoreCell), 6);
                float fall = 0f;
                for (int dj = -reach; dj <= reach; dj++)
                for (int di = -reach; di <= reach; di++)
                {
                    int ii = i + di, jj = j + dj;
                    if (ii >= 0 && jj >= 0 && ii < n && jj < n && !float.IsNaN(levels[jj * n + ii])) fall = MathF.Max(fall, MathF.Abs(levels[jj * n + ii] - lv));
                }
                tol += fall;
            }
            // Water shown where the volume has none, or higher than its level: standing proud of the land.
            if (shows && top > MathF.Max(g, land[k]) + tol && (float.IsNaN(lv) || top > lv + tol))
            {
                t.Floating++;
                t.Note("floating", $"water {top - g:F2} m over the ground at {x:F1},{z:F1} (its level {lv:F2}, drawn {top:F2}, {(kinds[k] == 1 ? "river" : "sea")})");
            }
            // The volume holds water here but the ground is what shows.
            if (!float.IsNaN(lv) && lv > g + tol && !shows)
            {
                t.Through++;
                t.Note("through", $"ground shows {lv - g:F2} m under the water's level at {x:F1},{z:F1} (drawn {top:F2})");
            }
            if (!shows) continue;
            foreach (int o in new[] { k - 1, k + 1, k - n, k + n })
            {
                if (o < 0 || o >= n * n) continue;
                // The water's own outline (no water drawn beside it) standing over the ground there.
                if (float.IsNaN(tops[o]) && MathF.Max(ground[o], land[o]) < top - ShoreTol)
                {
                    double ox = x0 + o % n * ShoreCell, oz = z0 + o / n * ShoreCell;
                    GroundSample gq = sw.S.Sampler.Sample(Mathx.WrapX(ox, sw.W.Width), Mathx.Wrap(oz, sw.W.Height), 1);
                    string at = $"water's outline {top - ground[o]:F2} m over the ground at {x:F1},{z:F1} (its level {lv:F2}; beside it a river at {gq.RiverLevel - SurfaceSampler.RiverDrop:F2}, ground {gq.Height:F2})";
                    // Beside it the ground belongs to another reach lower down: where the meander folds a river's course
                    // back on itself, or two run side by side down a gorge, the generator tears the channel between them.
                    // That is the volume's own wall (Water's checks hold it to a 1% share), not the drawn edge: noted apart.
                    // (Half a metre apart in level, half a metre apart on the ground, is no rapid of one river.)
                    if (MathF.Abs(gq.RiverLevel - SurfaceSampler.RiverDrop - lv) > 0.3f)
                    {
                        t.Torn++;
                        t.Note("torn", at);
                        torn[k] = torn[o] = true;
                    }
                    else
                    {
                        t.Cut++;
                        t.Note("cut", at);
                    }
                    break;
                }
            }
        }
        // Where river water meets the sea's, side by side, the two stand at one level.
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int k = j * n + i;
            if (kinds[k] != 1) continue;
            foreach (int o in new[] { k - 1, k + 1, k - n, k + n })
            {
                if (o < 0 || o >= n * n || kinds[o] != 2 || MathF.Abs(tops[k] - tops[o]) <= ShoreTol) continue;
                t.Stepped++;
                t.Note("step", $"step of {tops[k] - tops[o]:F2} m from river to sea at {x0 + i * ShoreCell:F1},{z0 + j * ShoreCell:F1}");
            }
        }
        var origin = new Vector2((float)x0, (float)z0);
        List<List<Vector2>> lines = Contours(drawn, n, ShoreCell, origin);
        var idealLines = Contours(ideal, n, ShoreCell, origin);
        // The ground's own line at the water's level, for how far the drawn edge strays from it.
        var idealPoints = new Dictionary<(int, int), List<Vector2>>();
        foreach (var line in idealLines)
            foreach (Vector2 p in Resample(line, 0.25f))
            {
                var key = ((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));
                if (!idealPoints.TryGetValue(key, out var list)) idealPoints[key] = list = new List<Vector2>();
                list.Add(p);
            }
        float Nearest(Dictionary<(int, int), List<Vector2>> grid, Vector2 p, int reach)
        {
            float best = float.MaxValue;
            int ix = (int)MathF.Floor(p.X), iz = (int)MathF.Floor(p.Y);
            for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
                if (grid.TryGetValue((ix + dx, iz + dz), out var list))
                    foreach (Vector2 q in list) best = MathF.Min(best, p.DistanceSquaredTo(q));
            return MathF.Sqrt(best);
        }
        // In the window (less a margin), off a place's own ground, and 2 m or more from a torn channel's wall.
        bool Inside(Vector2 p, float margin)
        {
            if (p.X <= x0 + margin || p.X >= x0 + 2 * ShoreHalf - margin || p.Y <= z0 + margin || p.Y >= z0 + 2 * ShoreHalf - margin) return false;
            int i = Math.Clamp((int)MathF.Round((p.X - (float)x0) / ShoreCell), 0, n - 1), j = Math.Clamp((int)MathF.Round((p.Y - (float)z0) / ShoreCell), 0, n - 1);
            if (placed[j * n + i]) return false;
            for (int dj = -4; dj <= 4; dj++)
            for (int di = -4; di <= 4; di++)
                if (i + di >= 0 && j + dj >= 0 && i + di < n && j + dj < n && torn[(j + dj) * n + i + di]) return false;
            return true;
        }
        Vector2 Slope(Vector2 p)
        {
            int i = Math.Clamp((int)MathF.Round((p.X - (float)x0) / ShoreCell), 1, n - 2), j = Math.Clamp((int)MathF.Round((p.Y - (float)z0) / ShoreCell), 1, n - 2);
            return new Vector2(ground[j * n + i + 1] - ground[j * n + i - 1], ground[(j + 1) * n + i] - ground[(j - 1) * n + i]) / (2 * ShoreCell);
        }
        // Where a line turns more sharply than SharpTurn between neighbouring samples (none on a puddle or islet of a metre or two).
        List<(Vector2 At, float Turn)> Turns(List<Vector2> line)
        {
            var turns = new List<(Vector2, float)>();
            if (Resample(line, 1f).Count < 6) return turns;
            List<Vector2> chords = Resample(line, ChordOf(level));
            for (int k = 0; k + 2 < chords.Count; k++)
            {
                if (!Inside(chords[k + 1], 1f)) continue;
                Vector2 a = (chords[k + 1] - chords[k]).Normalized(), b = (chords[k + 2] - chords[k + 1]).Normalized();
                float turn = Mathf.RadToDeg(MathF.Acos(Math.Clamp(a.Dot(b), -1f, 1f)));
                if (turn > SharpTurn) turns.Add((chords[k + 1], turn));
            }
            return turns;
        }
        // The land's own corners at the water's level: a drawn edge turning there follows the land.
        var corners = idealLines.SelectMany(Turns).Select(c => c.At).ToList();
        var samples = new List<Vector2>();
        foreach (var line in lines)
        {
            List<Vector2> fine = Resample(line, 0.5f);
            samples.AddRange(fine);
            foreach (Vector2 p in fine)
            {
                if (!Inside(p, 1f)) continue;
                t.Looked++;
                if (Nearest(idealPoints, p, 1) <= OffLine) continue;
                t.Off++;
                t.Note("off", $"edge off the land's line at {p.X:F1},{p.Y:F1}");
            }
            List<Vector2> metre = Resample(line, 1f);
            if (metre.Count < 6) continue;   // a puddle or an islet of a metre or two
            for (int k = 0; k + 1 < metre.Count; k++) if (Inside(metre[k], 0f)) t.Length += metre[k].DistanceTo(metre[k + 1]);
            foreach (var (at, turn) in Turns(line))
            {
                if (corners.Any(c => c.DistanceTo(at) <= ChordOf(level) * 1.5f)) continue;
                t.Sharp++;
                t.Note("turn", $"edge turns {turn:F0} degrees at {at.X:F1},{at.Y:F1}");
            }
            int run = (int)StraightRun;
            for (int k = 0; k + run < metre.Count; k++)
            {
                if (!Inside(metre[k], 1f) || !Inside(metre[k + run], 1f)) continue;
                Vector2 first = (metre[k + 1] - metre[k]).Normalized();
                bool straight = true;
                for (int s = 1; s < run && straight; s++)
                    straight = Mathf.RadToDeg(MathF.Acos(Math.Clamp(first.Dot((metre[k + s + 1] - metre[k + s]).Normalized()), -1f, 1f))) < 2f;
                // Straight, across the land's slope rather than along its level (the land's own line runs square to its
                // slope), and off the land's own line for most of it: a straight bank the land makes is the land's.
                Vector2 slope = Slope(metre[k + run / 2]);
                if (!straight || slope.Length() < 1f / 30f || MathF.Abs(first.Dot(slope.Normalized())) < 0.34f) continue;
                int off = 0;
                for (int s = 0; s <= run; s++) if (Nearest(idealPoints, metre[k + s], 1) > OffLine) off++;
                if (off * 2 <= run) continue;
                t.Straight++;
                t.Note("straight", $"edge straight for {StraightRun} m across land sloping {slope.Length():F2} at {metre[k].X:F1},{metre[k].Y:F1}");
                k += run;
            }
        }
        if (nearLine != null)
        {
            var mine = new Dictionary<(int, int), List<Vector2>>();
            foreach (Vector2 p in samples)
            {
                var key = ((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));
                if (!mine.TryGetValue(key, out var list)) mine[key] = list = new List<Vector2>();
                list.Add(p);
            }
            foreach (Vector2 p in nearLine)
                if (Inside(p, 10f)) t.Apart.Add(MathF.Min(Nearest(mine, p, 10), 10f));
        }
        return samples;
    }

    /// <summary>Points along a polyline every <paramref name="spacing"/> metres of its length.</summary>
    static List<Vector2> Resample(List<Vector2> line, float spacing)
    {
        var points = new List<Vector2> { line[0] };
        float carry = 0f;
        for (int k = 0; k + 1 < line.Count; k++)
        {
            Vector2 a = line[k], b = line[k + 1];
            float length = a.DistanceTo(b), at = spacing - carry;
            while (at <= length)
            {
                points.Add(a.Lerp(b, at / length));
                at += spacing;
            }
            carry = length - (at - spacing);
        }
        return points;
    }

    /// <summary>Where a field over a grid crosses zero (marching squares), joined into lines.</summary>
    static List<List<Vector2>> Contours(float[] f, int n, float cell, Vector2 origin)
    {
        // Edge ids: (j n + i) 2 runs from grid point (i, j) along x, + 1 along z.
        Vector2 Cross(int id)
        {
            int p = id >> 1, i = p % n, j = p / n;
            int q = (id & 1) == 0 ? p + 1 : p + n;
            float t = f[p] / (f[p] - f[q]);
            Vector2 a = origin + new Vector2(i, j) * cell;
            return a + ((id & 1) == 0 ? new Vector2(t * cell, 0) : new Vector2(0, t * cell));
        }
        var segments = new List<(int A, int B)>();
        for (int j = 0; j + 1 < n; j++)
        for (int i = 0; i + 1 < n; i++)
        {
            int p = j * n + i;
            float a = f[p], b = f[p + 1], c = f[p + n], d = f[p + n + 1];
            int code = (a > 0 ? 1 : 0) | (b > 0 ? 2 : 0) | (d > 0 ? 4 : 0) | (c > 0 ? 8 : 0);
            int e0 = p * 2, e1 = (p + 1) * 2 + 1, e2 = (p + n) * 2, e3 = p * 2 + 1;
            bool centre = a + b + c + d > 0f;
            switch (code)
            {
                case 1: case 14: segments.Add((e3, e0)); break;
                case 2: case 13: segments.Add((e0, e1)); break;
                case 3: case 12: segments.Add((e3, e1)); break;
                case 4: case 11: segments.Add((e1, e2)); break;
                case 6: case 9: segments.Add((e0, e2)); break;
                case 7: case 8: segments.Add((e3, e2)); break;
                case 5:
                    if (centre) { segments.Add((e0, e1)); segments.Add((e2, e3)); }
                    else { segments.Add((e3, e0)); segments.Add((e1, e2)); }
                    break;
                case 10:
                    if (centre) { segments.Add((e3, e0)); segments.Add((e1, e2)); }
                    else { segments.Add((e0, e1)); segments.Add((e2, e3)); }
                    break;
            }
        }
        var touching = new Dictionary<int, List<int>>();
        for (int s = 0; s < segments.Count; s++)
            foreach (int e in new[] { segments[s].A, segments[s].B })
            {
                if (!touching.TryGetValue(e, out var list)) touching[e] = list = new List<int>();
                list.Add(s);
            }
        var used = new bool[segments.Count];
        var lines = new List<List<Vector2>>();
        for (int s = 0; s < segments.Count; s++)
        {
            if (used[s]) continue;
            used[s] = true;
            var ids = new LinkedList<int>(new[] { segments[s].A, segments[s].B });
            // Grow from both ends.
            foreach (bool front in new[] { false, true })
                for (int end = front ? ids.First.Value : ids.Last.Value; ;)
                {
                    int next = touching[end].FirstOrDefault(o => !used[o], -1);
                    if (next < 0) break;
                    used[next] = true;
                    end = segments[next].A == end ? segments[next].B : segments[next].A;
                    if (front) ids.AddFirst(end);
                    else ids.AddLast(end);
                }
            lines.Add(ids.Select(Cross).ToList());
        }
        return lines;
    }
}
