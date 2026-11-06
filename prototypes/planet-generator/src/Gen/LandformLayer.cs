using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Planets.Gen;

/// <summary>A delta: a fan of distributaries from an apex inland, opening along an axis towards the sea.</summary>
public readonly record struct DeltaFan(double ApexX, double ApexY, float AxisX, float AxisY, float Radius, float Flow, int Id);

/// <summary>
/// The landform region layer: which landform holds where, and how strongly, all over a world.
///
/// Built once per world from its spec, before any height exists, on a coarse grid (about 256 m):
/// 1. <b>Context.</b> The continent's large shape gives land, sea and a rough sea level; then the
///    distance to the coast, long uplift belts, temperature, dryness (dry belts, distance from the
///    sea, rain shadow behind ranges on the prevailing wind), and a rough drainage map that says
///    where the big rivers reach the sea.
/// 2. <b>Mountains</b> follow the uplift belts, as long ranges whose edges grade into foothills.
/// 3. <b>Regions.</b> The world is cut into irregular regions (a jittered, warped Voronoi). Inland
///    landforms claim the regions that suit them best, each up to its share of the land, with a
///    pull towards neighbours of the same landform so they form whole countries. Coastal
///    landforms (fjords, cliffs, archipelagos) claim coastal regions, and deltas sit at the
///    mouths of the biggest rivers.
/// 4. <b>Blending.</b> Neighbouring regions share weight over a band, and a sample is the bilinear
///    blend of the grid around it, looked up through a gentle warp so borders wander.
///
/// Every step wraps on both axes, so the layer is as seamless as the noise.
/// </summary>
public sealed class LandformLayer
{
    public const double TargetNode = 256;
    const int Slots = 4;
    const float ShelfDepth = 70f;

    public readonly LandformMix Mix;
    public readonly int Nx, Ny;
    public readonly double NodeW, NodeH, Width, Height;
    /// <summary>Raw continent height at the rough sea level the regions were placed by.</summary>
    public readonly float ProtoSea;
    /// <summary>Region size in metres.</summary>
    public readonly double RegionSize;
    public readonly IReadOnlyList<DeltaFan> Deltas;
    /// <summary>The prevailing wind's direction (unit vector, towards where it blows), which the dunes lie square to.</summary>
    public readonly float WindX, WindY;

    readonly Torus _torus;
    readonly byte[] _l;          // Nx * Ny * Slots landforms
    readonly float[] _w;         // their weights, strongest first
    readonly float[] _coast;     // signed distance to the rough coast, metres (+ inland)
    readonly float[] _dry;       // dryness relative to the world's land, about -0.5..0.5
    readonly float[] _base;      // the continent's height averaged over a couple of kilometres (raw units)
    readonly Fractal _borderWarp;
    readonly LandformRelief[] _recipes;

    /// <summary>How many regions the world is divided into, each way: a bigger world has more of them (and bigger ones up to 4 km), never stretched ones.</summary>
    public int RegionsX => _rnx;
    public int RegionsY => _rny;

    /// <summary>The landform per region (index ry * regionsX + rx), for views and tests.</summary>
    public readonly Landform[] RegionLandform;
    /// <summary>The coastal landform per region, or 255 for none.</summary>
    public readonly byte[] RegionCoast;
    readonly int _rnx, _rny;
    readonly double _rw, _rh;
    readonly ulong _seed;

    internal LandformLayer(TerrainModel model, LandformMix mix, Func<double, double, double, float> continent, float landTarget)
    {
        Mix = mix;
        Width = model.Width;
        Height = model.Height;
        _torus = model.Torus;
        _seed = model.Spec.Seed;
        ArchetypeDef def = model.Def;
        Nx = Math.Max(4, (int)Math.Round(Width / TargetNode));
        Ny = Math.Max(2, (int)Math.Round(Height / TargetNode));
        NodeW = Width / Nx;
        NodeH = Height / Ny;
        int n = Nx * Ny;
        ulong Salt(int k) => Hash.Of(_seed, 40 + k, (long)def.Archetype, def.Variant);
        _borderWarp = new Fractal(Salt(5), 1400, 2, 0.5f);
        _recipes = new LandformRelief[LandformInfo.Count];
        foreach (Landform l in LandformInfo.All) _recipes[(int)l] = Landforms.Recipe(l, def);

        // Prevailing wind for the dunes: east-west give or take, per world.
        var wrng = new Rng(Hash.Of(_seed, 0x57494E44));
        float windAngle = wrng.Range(-0.6f, 0.6f) + (wrng.NextFloat() < 0.5f ? 0f : MathF.PI);
        WindX = MathF.Cos(windAngle);
        WindY = MathF.Sin(windAngle);

        // ---- 1. Context ------------------------------------------------------------------
        var b = new float[n];
        Parallel.For(0, Ny, j =>
        {
            for (int i = 0; i < Nx; i++) b[j * Nx + i] = continent(X(i), Y(j), NodeW);
        });
        ProtoSea = Quantile(b, 1f - landTarget);
        // The level open ground is flattened towards: the continent averaged over a couple of
        // kilometres, with the sea counted at sea level so coastal plains don't tilt down into it.
        var level = new float[n];
        for (int k = 0; k < n; k++) level[k] = Math.Max(b[k], ProtoSea);
        _base = Blur(level, 2200);
        var land = new bool[n];
        for (int k = 0; k < n; k++) land[k] = b[k] > ProtoSea;
        _coast = SignedCoastDistance(land);

        // Uplift belts: long, winding bands along the zero lines of a warped low-frequency field.
        // The wavelength stays well under the planet's circumference both ways: a field much longer
        // than that collapses on the torus into a few sinusoids, whose level lines run straight.
        double beltWave = Math.Clamp(Math.Min(Width, Height * 2) * 0.2, 2600, 7000);
        var beltField = new Fractal(Salt(0), beltWave, 2, 0.4f);
        var beltWarp = new Fractal(Salt(1), beltWave * 0.55, 3, 0.5f);
        var passes = new Fractal(Salt(2), 1800, 2, 0.5f);
        var dryNoise = new Fractal(Salt(3), 2200, 3, 0.5f);
        var u = new float[n];
        var line0 = new float[n];
        var noise = new float[n];
        Parallel.For(0, Ny, j =>
        {
            for (int i = 0; i < Nx; i++)
            {
                int k = j * Nx + i;
                P4 p = _torus.At(X(i), Y(j));
                float w = beltWarp.Fbm(p, NodeW) * (float)beltWave * 0.35f, w2 = beltWarp.Fbm(p.Swap, NodeW) * (float)beltWave * 0.35f;
                var q = new P4(p.X + w, p.Y - w2, p.Z + w2 * 0.8, p.W - w * 0.6);
                float signed = beltField.Fbm(q, NodeW);
                line0[k] = signed;
                float line = 1f - Math.Abs(signed);

                // Ranges stand back from the shore a little, leaving coastal lowland (except on cold coasts, where fjords take over).
                line -= 0.08f * Mathx.Smooth(2200f, 0f, _coast[k]);
                u[k] = line;
                noise[k] = dryNoise.Fbm(p, NodeW);
            }
        });

        bool single = mix.Single;
        bool ranges = !single && mix.Has(Landform.Mountains);
        // Mountain ranges: spines traced along the strongest uplift lines, each several kilometres
        // long, with the range in a band round its spine and foothills in a wider band outside.
        // The range sags at intervals along its spine: passes. The mix's mountain share sets how
        // much spine there is, so a mountain world gets more and longer ranges, never blobs.
        var mountain = new float[n];
        var foothill = new float[n];
        if (ranges) TraceRanges(mix, land, u, line0, passes, mountain, foothill);

        // Rough height, temperature and latitude.
        var e = new float[n];
        var temp = new float[n];
        var lat = new float[n];
        for (int j = 0; j < Ny; j++)
        for (int i = 0; i < Nx; i++)
        {
            int k = j * Nx + i;
            lat[k] = (float)(Y(j) / Height) * 2f - 1f;
            e[k] = Math.Max(0f, b[k] - ProtoSea) + mountain[k] * def.MountainHeight * 0.7f;
            temp[k] = def.BaseTemperature - def.LatitudeRange * lat[k] * lat[k] - e[k] * 0.0005f + noise[k] * 0.03f;
        }

        // Dryness: subtropical dry belts, continental interiors, and rain shadows downwind of ranges.
        var dry = new float[n];
        int shadowSteps = (int)Math.Ceiling(6000 / NodeW);
        for (int j = 0; j < Ny; j++)
        for (int i = 0; i < Nx; i++)
        {
            int k = j * Nx + i;
            float al = Math.Abs(lat[k]);
            float belt = MathF.Exp(-MathF.Pow((al - 0.33f) / 0.13f, 2f));
            float interior = Mathx.Smooth(0f, 5000f, _coast[k]);
            // Trade winds blow from the east near the equator, westerlies at mid latitudes, polar
            // easterlies near the poles: the shadow lies downwind of any range in the way.
            int upwind = al < 0.33f || al > 0.7f ? 1 : -1;
            float shadow = 0f;
            for (int s = 1; s <= shadowSteps; s++)
                shadow = Math.Max(shadow, mountain[Index(i + upwind * s, j)] * MathF.Exp(-s * (float)NodeW / 3500f));
            shadow *= 1f - mountain[k];
            dry[k] = 0.25f * belt + 0.3f * interior + 0.45f * shadow + 0.12f * noise[k];
        }
        // Relative to the world's own land, so every world's deserts go to its driest places.
        float dryMean = 0f;
        int landCount = 0;
        for (int k = 0; k < n; k++) if (land[k]) { dryMean += dry[k]; landCount++; }
        dryMean = landCount > 0 ? dryMean / landCount : 0f;
        _dry = new float[n];
        for (int k = 0; k < n; k++) _dry[k] = Math.Clamp(dry[k] - dryMean, -0.6f, 0.6f);
        float[] dryRank = Ranks(dry, land);
        float[] eRank = Ranks(e, land);

        // Rough drainage: where do the big rivers reach the sea?
        int[] down = Drainage(e, land, dry, out float[] flow);

        // ---- 2-3. Regions ----------------------------------------------------------------
        RegionSize = Math.Clamp(Math.Sqrt(Width * Height / 32.0), 1600, 4000);
        _rnx = Math.Max(2, (int)Math.Round(Width / RegionSize));
        _rny = Math.Max(1, (int)Math.Round(Height / RegionSize));
        _rw = Width / _rnx;
        _rh = Height / _rny;
        int regions = _rnx * _rny;
        var regionWarp = new Fractal(Salt(4), RegionSize * 0.9, 2, 0.5f);
        double blend = RegionSize * 0.16;

        // Soft region membership per node: the nearest few seeds, by warped distance.
        var near = new (int r, float w)[n][];
        var owner = new int[n];
        Parallel.For(0, Ny, j =>
        {
            Span<(int r, float d)> cand = stackalloc (int, float)[25];
            for (int i = 0; i < Nx; i++)
            {
                int k = j * Nx + i;
                double x = X(i), y = Y(j);
                P4 p = _torus.At(x, y);
                double wx = x + regionWarp.Fbm(p, NodeW) * RegionSize * 0.3, wy = y + regionWarp.Fbm(p.Swap, NodeW) * RegionSize * 0.3;
                int ci = (int)Math.Floor(Mathx.WrapX(wx, Width) / _rw), cj = (int)Math.Floor(Mathx.Wrap(wy, Height) / _rh);
                int count = 0;
                float best = float.MaxValue;
                for (int dj = -2; dj <= 2; dj++)
                for (int di = -2; di <= 2; di++)
                {
                    int ri = ci + di, rj = cj + dj;
                    Seed(ri, rj, out double sx, out double sy);
                    double dx = Mathx.WrapDelta(wx - sx, Width), dy = Mathx.WrapDelta(wy - sy, Height);
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    cand[count++] = (RegionIndex(ri, rj), d);
                    best = Math.Min(best, d);
                }
                var list = new List<(int r, float w)>(3);
                foreach (var (r, d) in cand[..count])
                {
                    float w = Mathx.Smooth((float)blend, 0f, d - best);
                    if (w <= 0f) continue;
                    int at = list.FindIndex(t => t.r == r);
                    if (at >= 0) list[at] = (r, Math.Max(list[at].w, w));
                    else list.Add((r, w));
                }
                float sum = 0f;
                foreach (var t in list) sum += t.w;
                var arr = new (int r, float w)[list.Count];
                int top = 0;
                for (int t = 0; t < list.Count; t++)
                {
                    arr[t] = (list[t].r, list[t].w / sum);
                    if (arr[t].w > arr[top].w) top = t;
                }
                near[k] = arr;
                owner[k] = arr[top].r;
            }
        });

        // Region context, from the nodes each owns.
        var ctx = new RegionContext[regions];
        for (int k = 0; k < n; k++)
        {
            ref RegionContext c = ref ctx[owner[k]];
            if (land[k])
            {
                float free = 1f - mountain[k] - foothill[k];
                c.Land++;
                c.Free += free;
                c.Dry += dryRank[k];
                c.Elev += eRank[k];
                c.Temp += temp[k];
                c.Belt += mountain[k];
                c.Lat += Math.Abs(lat[k]);
                c.Flow = Math.Max(c.Flow, flow[k]);
                if (_coast[k] < 1500f) c.CoastLand++;
            }
            else if (_coast[k] > -3500f && b[k] > ProtoSea - ShelfDepth) c.Shelf++;
        }
        float maxFlow = 1e-6f;
        foreach (RegionContext c in ctx) maxFlow = Math.Max(maxFlow, c.Flow);
        for (int r = 0; r < regions; r++)
        {
            ref RegionContext c = ref ctx[r];
            if (c.Land > 0)
            {
                c.Dry /= c.Land; c.Elev /= c.Land; c.Temp /= c.Land; c.Belt /= c.Land; c.Lat /= c.Land;
            }
            c.Flow = MathF.Sqrt(c.Flow / maxFlow);
            c.Jitter = Hash.Unit(Hash.Of(_seed, r, 0x4A4954), 0);
        }

        RegionLandform = new Landform[regions];
        RegionCoast = new byte[regions];
        Array.Fill(RegionCoast, (byte)255);
        float foothillArea = 0f;
        for (int k = 0; k < n; k++) if (land[k]) foothillArea += foothill[k];
        AssignInland(mix, ctx, ranges, foothillArea);
        if (!single) AssignCoasts(mix, ctx);

        // Deltas at the mouths of the biggest rivers.
        var deltas = new List<DeltaFan>();
        if (!single && mix.Has(Landform.Delta)) PlaceDeltas(mix, land, down, flow, temp, mountain, deltas);
        Deltas = deltas;

        // ---- 4. Node weights -------------------------------------------------------------
        _l = new byte[n * Slots];
        _w = new float[n * Slots];
        Parallel.For(0, n, k =>
        {
            var blendHere = new LandformBlend();
            if (single) blendHere = LandformBlend.Of(mix.Dominant);
            else
            {
                foreach (var (r, w) in near[k]) blendHere.Add(RegionLandform[r], w);
                if (ranges && foothill[k] + mountain[k] > 0f)
                {
                    Overlay(ref blendHere, Landform.Hills, foothill[k] + mountain[k]);
                    Overlay(ref blendHere, Landform.Mountains, mountain[k]);
                }
                // Coastal landforms along their regions' coasts.
                float fj = 0f, cl = 0f, ar = 0f;
                foreach (var (r, w) in near[k])
                {
                    switch (RegionCoast[r])
                    {
                        case (byte)Landform.Fjords: fj += w; break;
                        case (byte)Landform.CoastalCliffs: cl += w; break;
                        case (byte)Landform.Archipelago: ar += w; break;
                    }
                }
                float d = _coast[k];
                if (fj > 0f) Overlay(ref blendHere, Landform.Fjords, fj * Mathx.Smooth(-1600f, -800f, d) * Mathx.Smooth(2400f, 1400f, d));
                if (cl > 0f) Overlay(ref blendHere, Landform.CoastalCliffs, cl * Mathx.Smooth(-700f, -350f, d) * Mathx.Smooth(1100f, 600f, d));
                if (ar > 0f) Overlay(ref blendHere, Landform.Archipelago, ar * Mathx.Smooth(-4200f, -3000f, d) * Mathx.Smooth(900f, 300f, d));
                foreach (DeltaFan f in deltas) Overlay(ref blendHere, Landform.Delta, FanWeight(f, X(k % Nx), Y(k / Nx), d));
            }
            // An archipelago world is islands everywhere.
            if (mix.Dominant == Landform.Archipelago && mix.Single) blendHere = LandformBlend.Of(Landform.Archipelago);
            blendHere.Normalize();
            for (int s = 0; s < Slots; s++)
            {
                _l[k * Slots + s] = (byte)(s < blendHere.Count ? blendHere.LandformAt(s) : 0);
                _w[k * Slots + s] = s < blendHere.Count ? blendHere.WeightAt(s) : 0f;
            }
        });
    }

    // ---- Queries ---------------------------------------------------------------------------

    /// <summary>The landforms at a point in metres (x east, y south; both wrap), strongest first.</summary>
    public LandformBlend At(double x, double y) => At(x, y, _torus.At(x, y));

    /// <summary>The same, given the point's place on the torus (saves recomputing it).</summary>
    public LandformBlend At(double x, double y, in P4 p)
    {
        var s = new Stencil(this, x, y, p);
        return Blend(s);
    }

    /// <summary>The 3 x 3 nodes round a point and their B-spline weights: one lookup, read by every field.</summary>
    readonly struct Stencil
    {
        public readonly int I0, I1, I2, J0, J1, J2;
        public readonly float X0, X1, X2, Y0, Y1, Y2;

        public Stencil(LandformLayer layer, double x, double y, in P4 p)
        {
            layer.Lookup(x, y, p, out int ci, out int cj, out float tx, out float ty);
            Spline(tx, out X0, out X1, out X2);
            Spline(ty, out Y0, out Y1, out Y2);
            int nx = layer.Nx, ny = layer.Ny;
            I0 = ((ci - 1) % nx + nx) % nx; I1 = (ci % nx + nx) % nx; I2 = ((ci + 1) % nx + nx) % nx;
            J0 = ((cj - 1) % ny + ny) % ny * nx; J1 = (cj % ny + ny) % ny * nx; J2 = ((cj + 1) % ny + ny) % ny * nx;
        }

        public float Read(float[] f) =>
            (f[J0 + I0] * X0 + f[J0 + I1] * X1 + f[J0 + I2] * X2) * Y0 +
            (f[J1 + I0] * X0 + f[J1 + I1] * X1 + f[J1 + I2] * X2) * Y1 +
            (f[J2 + I0] * X0 + f[J2 + I1] * X1 + f[J2 + I2] * X2) * Y2;
    }

    LandformBlend Blend(in Stencil s)
    {
        Span<float> acc = stackalloc float[LandformInfo.Count];
        acc.Clear();
        Gather(acc, s.J0 + s.I0, s.X0 * s.Y0); Gather(acc, s.J0 + s.I1, s.X1 * s.Y0); Gather(acc, s.J0 + s.I2, s.X2 * s.Y0);
        Gather(acc, s.J1 + s.I0, s.X0 * s.Y1); Gather(acc, s.J1 + s.I1, s.X1 * s.Y1); Gather(acc, s.J1 + s.I2, s.X2 * s.Y1);
        Gather(acc, s.J2 + s.I0, s.X0 * s.Y2); Gather(acc, s.J2 + s.I1, s.X1 * s.Y2); Gather(acc, s.J2 + s.I2, s.X2 * s.Y2);
        // Keep the strongest four. Dropping a fifth outright would be a jump where it changes
        // places with the fourth, so everything is lowered by the fifth's weight (and a small
        // floor) instead: what is dropped is already at zero, and the blend stays continuous.
        float w1 = 0f, w2 = 0f, w3 = 0f, w4 = 0f, w5 = 0f;
        for (int l = 0; l < acc.Length; l++)
        {
            float v = acc[l];
            if (v <= w5) continue;
            if (v > w1) { w5 = w4; w4 = w3; w3 = w2; w2 = w1; w1 = v; }
            else if (v > w2) { w5 = w4; w4 = w3; w3 = w2; w2 = v; }
            else if (v > w3) { w5 = w4; w4 = w3; w3 = v; }
            else if (v > w4) { w5 = w4; w4 = v; }
            else w5 = v;
        }
        float floor = Math.Max(w5, 0.002f);
        var blend = new LandformBlend();
        for (int l = 0; l < acc.Length; l++)
            if (acc[l] > floor) blend.Add((Landform)l, acc[l] - floor);
        blend.Normalize();
        return blend;
    }

    /// <summary>The dominant landform at a point.</summary>
    public Landform Dominant(double x, double y) => At(x, y).Dominant;

    /// <summary>Signed distance (metres) to the rough coast the regions were placed by: positive inland.</summary>
    public float CoastDistance(double x, double y) => Field(_coast, x, y, _torus.At(x, y));

    /// <summary>How much drier than the world's average land a point is (about -0.5 to 0.5): dry belts, interiors, rain shadows.</summary>
    public float Dryness(double x, double y) => Field(_dry, x, y, _torus.At(x, y));

    /// <summary>The blended relief recipe for a blend of landforms.</summary>
    public LandformRelief Relief(in LandformBlend blend)
    {
        var r = new LandformRelief();
        for (int i = 0; i < blend.Count; i++) r.Add(_recipes[(int)blend.LandformAt(i)], blend.WeightAt(i));
        return r;
    }

    /// <summary>One landform's recipe on this world.</summary>
    public LandformRelief Recipe(Landform landform) => _recipes[(int)landform];

    internal void Sample(double x, double y, in P4 p, out LandformBlend blend, out float coast, out float dry)
    {
        var s = new Stencil(this, x, y, p);
        blend = Blend(s);
        coast = s.Read(_coast);
        dry = s.Read(_dry);
    }

    /// <summary>Everything the terrain needs at one point, from one lookup.</summary>
    internal void Sample(double x, double y, in P4 p, out LandformBlend blend, out float coast, out float dry, out float baseLevel)
    {
        var s = new Stencil(this, x, y, p);
        blend = Blend(s);
        coast = s.Read(_coast);
        dry = s.Read(_dry);
        baseLevel = s.Read(_base);
    }

    /// <summary>The continent's raw height averaged over a couple of kilometres: the level open ground is flattened towards.</summary>
    public float BaseLevel(double x, double y) => Field(_base, x, y, _torus.At(x, y));

    internal float BaseLevel(double x, double y, in P4 p) => Field(_base, x, y, p);

    /// <summary>A wrapping box blur, run three times (close to a Gaussian), with the given radius in metres.</summary>
    float[] Blur(float[] src, double radius)
    {
        int rx = Math.Max(1, (int)Math.Round(radius / NodeW)), ry = Math.Max(1, (int)Math.Round(radius / NodeH));
        var a = (float[])src.Clone();
        var t = new float[a.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            for (int j = 0; j < Ny; j++)
            for (int i = 0; i < Nx; i++)
            {
                float s = 0f;
                for (int d = -rx; d <= rx; d++) s += a[Index(i + d, j)];
                t[j * Nx + i] = s / (2 * rx + 1);
            }
            for (int j = 0; j < Ny; j++)
            for (int i = 0; i < Nx; i++)
            {
                float s = 0f;
                for (int d = -ry; d <= ry; d++) s += t[Index(i, j + d)];
                a[j * Nx + i] = s / (2 * ry + 1);
            }
        }
        return a;
    }

    /// <summary>Weights at a grid node, for tests and views: the landforms as the regions were laid out, before the warp.</summary>
    public LandformBlend Node(int i, int j)
    {
        int k = Index(i, j);
        var blend = new LandformBlend();
        for (int s = 0; s < Slots; s++)
            if (_w[k * Slots + s] > 0f) blend.Add((Landform)_l[k * Slots + s], _w[k * Slots + s]);
        return blend;
    }

    public float NodeCoast(int i, int j) => _coast[Index(i, j)];
    public double NodeX(int i) => X(i);
    public double NodeY(int j) => Y(j);

    /// <summary>
    /// The landforms that really hold some of this world's land, with their share of it (by node
    /// weight on dry ground), largest first. Coastal landforms only line coasts, so their shares are small.
    /// </summary>
    public List<(Landform landform, float share)> Present(float minShare = 0.003f)
    {
        var sum = new double[LandformInfo.Count];
        double total = 0;
        for (int k = 0; k < Nx * Ny; k++)
        {
            if (_coast[k] <= 0f) continue;
            for (int s = 0; s < Slots; s++) sum[_l[k * Slots + s]] += _w[k * Slots + s];
            total += 1;
        }
        var list = new List<(Landform, float)>();
        for (int l = 0; l < sum.Length; l++)
            if (total > 0 && sum[l] / total >= minShare) list.Add(((Landform)l, (float)(sum[l] / total)));
        list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return list;
    }

    /// <summary>A hash of every node's landforms and weights, for determinism checks.</summary>
    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Add(uint v) { h = (h ^ v) * 1099511628211UL; }
        for (int k = 0; k < _l.Length; k++)
        {
            Add(_l[k]);
            Add((uint)BitConverter.SingleToInt32Bits(_w[k]));
        }
        foreach (DeltaFan f in Deltas) Add((uint)BitConverter.SingleToInt32Bits((float)(f.ApexX * 3 + f.ApexY)));
        return h;
    }

    // ---- Ranges ----------------------------------------------------------------------------

    /// <summary>Range spines as polylines (metres, unwrapped), for views and tests.</summary>
    public IReadOnlyList<IReadOnlyList<(double x, double y)>> Ranges => _ranges;
    readonly List<IReadOnlyList<(double x, double y)>> _ranges = new();

    void TraceRanges(LandformMix mix, bool[] land, float[] u, float[] field, Fractal passes, float[] mountain, float[] foothill)
    {
        int n = land.Length, landNodes = 0;
        foreach (bool l in land) if (l) landNodes++;
        double landArea = landNodes * NodeW * NodeH;
        float share = Math.Clamp(mix.Share(Landform.Mountains), 0.06f, 0.4f);
        // Half-width of a range's core, and of its foothill band, in metres.
        double core = Math.Clamp(Math.Min(Width, Height * 2) / 22.0, 550, 1500), outer = core * 2.0;
        // The share covers the whole mountain country: the ranges and their foothills.
        double wantLength = share * landArea / (2 * outer * 0.65), length = 0;
        double spineLength = Math.Clamp(Math.Min(Width, Height * 2) * 0.4, 4000, 16000);
        var order = new List<int>();
        for (int k = 0; k < n; k++) if (land[k]) order.Add(k);
        order.Sort((a, b) => u[b] != u[a] ? u[b].CompareTo(u[a]) : a.CompareTo(b));
        var points = new List<(double x, double y, double width, float pass)>();
        var segs = new List<(int a, int b)>();
        var rng = new Rng(Hash.Of(_seed, 0x52414E47));
        foreach (int start in order)
        {
            if (length >= wantLength) break;
            double sx = X(start % Nx), sy = Y(start / Nx);
            bool clear = true;
            foreach (var pt in points)
            {
                double dx = Mathx.WrapDelta(sx - pt.x, Width), dy = Mathx.WrapDelta(sy - pt.y, Height);
                if (dx * dx + dy * dy < Math.Pow(outer * 1.6, 2)) { clear = false; break; }
            }
            if (!clear) continue;
            double width = core * rng.Range(0.8f, 1.25f), want = spineLength * rng.Range(0.6f, 1.2f);
            var spine = new List<(double x, double y)>();
            // Trace both ways from the start along the uplift line (a zero contour of the belt field).
            for (int dir = -1; dir <= 1; dir += 2)
            {
                var half = new List<(double x, double y)>();
                double x = sx, y = sy;
                GradientAt(field, x, y, out double gx, out double gy);
                double tx = -gy * dir, ty = gx * dir, tl = Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-9) { tx = dir; ty = 0; tl = 1; }
                tx /= tl; ty /= tl;
                int wet = 0;
                for (double walked = 0; walked < want * 0.5; walked += 160)
                {
                    GradientAt(field, x, y, out gx, out gy);
                    double g2 = gx * gx + gy * gy;
                    double nx = -gy, ny = gx, nl = Math.Sqrt(g2);
                    if (nl > 1e-9)
                    {
                        nx /= nl; ny /= nl;
                        if (nx * tx + ny * ty < 0) { nx = -nx; ny = -ny; }
                        // Turn gently towards the contour's own heading.
                        tx = tx * 0.6 + nx * 0.4;
                        ty = ty * 0.6 + ny * 0.4;
                        double l = Math.Sqrt(tx * tx + ty * ty);
                        tx /= l; ty /= l;
                    }
                    x += tx * 160;
                    y += ty * 160;
                    // Pull back onto the line (a Newton step towards the zero contour).
                    if (g2 > 1e-12)
                    {
                        double v = Sample(field, x, y);
                        double k = Math.Clamp(v / g2, -60, 60);
                        x -= gx * k * 0.5;
                        y -= gy * k * 0.5;
                    }
                    bool onLand = land[Index((int)Math.Floor(Mathx.WrapX(x, Width) / NodeW), (int)Math.Floor(Mathx.Wrap(y, Height) / NodeH))];
                    wet = onLand ? 0 : wet + 1;
                    if (wet > 2) break; // ranges end at the coast
                    half.Add((x, y));
                }
                if (dir < 0) half.Reverse();
                if (dir < 0) { spine.AddRange(half); spine.Add((sx, sy)); }
                else spine.AddRange(half);
            }
            if (spine.Count < 6) continue;
            int first = points.Count;
            double arc = 0;
            for (int i = 0; i < spine.Count; i++)
            {
                if (i > 0) arc += 160;
                // Passes: the range sags here and there along its length.
                P4 pp = _torus.At(Mathx.WrapX(spine[i].x, Width), Mathx.Wrap(spine[i].y, Height));
                float pass = 0.55f + 0.45f * Mathx.Smooth(-0.35f, 0.25f, passes.Fbm(pp, NodeW));
                // Ranges taper at their ends.
                double end = Math.Min(arc, (spine.Count - 1) * 160 - arc);
                pass *= (float)Math.Clamp(0.35 + end / 1500.0, 0.35, 1.0);
                points.Add((spine[i].x, spine[i].y, width, pass));
                if (i > 0) segs.Add((first + i - 1, first + i));
            }
            length += (spine.Count - 1) * 160;
            _ranges.Add(spine);
        }

        // Each node's weight from its nearest spine segment, rasterised segment by segment.
        var best = new float[n];
        Array.Fill(best, float.MaxValue);
        var bestWidth = new float[n];
        var bestPass = new float[n];
        foreach (var (ia, ib) in segs)
        {
            var A = points[ia];
            var B = points[ib];
            double bx = A.x + Mathx.WrapDelta(B.x - A.x, Width), by = A.y + Mathx.WrapDelta(B.y - A.y, Height);
            double reach = Math.Max(A.width, B.width) * 2.05;
            int i0 = (int)Math.Floor((Math.Min(A.x, bx) - reach) / NodeW), i1 = (int)Math.Ceiling((Math.Max(A.x, bx) + reach) / NodeW);
            int j0 = (int)Math.Floor((Math.Min(A.y, by) - reach) / NodeH), j1 = (int)Math.Ceiling((Math.Max(A.y, by) + reach) / NodeH);
            double vx = bx - A.x, vy = by - A.y, len2 = vx * vx + vy * vy;
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                double px = (i + 0.5) * NodeW, py = (j + 0.5) * NodeH;
                double t = len2 > 0 ? Math.Clamp(((px - A.x) * vx + (py - A.y) * vy) / len2, 0, 1) : 0;
                double dx = px - (A.x + vx * t), dy = py - (A.y + vy * t);
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                int k = Index(i, j);
                if (d >= best[k]) continue;
                best[k] = d;
                bestWidth[k] = (float)(A.width + (B.width - A.width) * t);
                bestPass[k] = A.pass + (B.pass - A.pass) * (float)t;
            }
        }
        for (int k = 0; k < n; k++)
        {
            if (best[k] == float.MaxValue) continue;
            float w = bestWidth[k];
            mountain[k] = Mathx.Smooth(w, w * 0.3f, best[k]) * bestPass[k];
            foothill[k] = Math.Max(0f, Mathx.Smooth(w * 2f, w * 0.9f, best[k]) - mountain[k]);
        }
    }

    float Sample(float[] f, double x, double y)
    {
        double gx = Mathx.WrapX(x, Width) / NodeW - 0.5, gy = Mathx.Wrap(y, Height) / NodeH - 0.5;
        int i = (int)Math.Floor(gx), j = (int)Math.Floor(gy);
        float tx = (float)(gx - i), ty = (float)(gy - j);
        return Mathx.Lerp(Mathx.Lerp(f[Index(i, j)], f[Index(i + 1, j)], tx), Mathx.Lerp(f[Index(i, j + 1)], f[Index(i + 1, j + 1)], tx), ty);
    }

    void GradientAt(float[] f, double x, double y, out double gx, out double gy)
    {
        gx = (Sample(f, x + NodeW, y) - Sample(f, x - NodeW, y)) / (2 * NodeW);
        gy = (Sample(f, x, y + NodeH) - Sample(f, x, y - NodeH)) / (2 * NodeH);
    }

    // ---- Internals -------------------------------------------------------------------------

    double X(int i) => (i + 0.5) * NodeW;
    double Y(int j) => (j + 0.5) * NodeH;
    int Index(int i, int j) => ((j % Ny + Ny) % Ny) * Nx + (i % Nx + Nx) % Nx;

    /// <summary>
    /// The node nearest a point (looked up through a gentle warp, so borders wander rather than
    /// follow the grid) and where the point sits in it, 0-1 each way, for a quadratic B-spline
    /// over the 3 x 3 nodes round it. Unlike bilinear blending it has no creases along grid lines,
    /// which would show as straight lines in the relief, and unlike cubic blending it never overshoots.
    /// </summary>
    void Lookup(double x, double y, in P4 p, out int ci, out int cj, out float tx, out float ty)
    {
        double wx = x + _borderWarp.Fbm(p, 64) * 160.0, wy = y + _borderWarp.Fbm(p.Swap, 64) * 160.0;
        double gx = wx / NodeW - 0.5, gy = wy / NodeH - 0.5;
        double fx = Math.Floor(gx + 0.5), fy = Math.Floor(gy + 0.5);
        ci = (int)fx;
        cj = (int)fy;
        tx = (float)(gx - fx + 0.5);
        ty = (float)(gy - fy + 0.5);
    }

    static void Spline(float t, out float w0, out float w1, out float w2)
    {
        w0 = 0.5f * (1f - t) * (1f - t);
        w2 = 0.5f * t * t;
        w1 = 1f - w0 - w2;
    }

    float Field(float[] f, double x, double y, in P4 p) => new Stencil(this, x, y, p).Read(f);

    void Gather(Span<float> acc, int k, float w)
    {
        if (w <= 0f) return;
        for (int s = 0; s < Slots; s++)
        {
            float v = _w[k * Slots + s];
            if (v > 0f) acc[_l[k * Slots + s]] += v * w;
        }
    }

    static void Overlay(ref LandformBlend blend, Landform l, float w)
    {
        if (w <= 0f) return;
        w = Math.Min(w, 1f);
        blend.Normalize();
        var result = new LandformBlend();
        for (int i = 0; i < blend.Count; i++) result.Add(blend.LandformAt(i), blend.WeightAt(i) * (1f - w));
        result.Add(l, w);
        blend = result;
    }

    int RegionIndex(int ri, int rj) => ((rj % _rny + _rny) % _rny) * _rnx + (ri % _rnx + _rnx) % _rnx;

    /// <summary>A region's seed point, in the unwrapped cell (so distances across a seam come out right).</summary>
    void Seed(int ri, int rj, out double x, out double y)
    {
        int wi = (ri % _rnx + _rnx) % _rnx, wj = (rj % _rny + _rny) % _rny;
        ulong h = Hash.Of(_seed, wi, wj, 0x52454749);
        x = (ri + 0.15 + 0.7 * Hash.Unit(h, 1)) * _rw;
        y = (rj + 0.15 + 0.7 * Hash.Unit(h, 2)) * _rh;
    }

    struct RegionContext
    {
        public int Land, CoastLand, Shelf;
        public float Free, Dry, Elev, Temp, Belt, Lat, Flow, Jitter;
    }

    /// <summary>How strongly a landform depends on where it is: the pickiest claim their regions first.</summary>
    static int Pickiness(Landform l) => l switch
    {
        Landform.DuneSea or Landform.IcePlain or Landform.Wetlands => 5,
        Landform.VolcanicField or Landform.LakeDistrict or Landform.RiverValleys or Landform.Karst => 4,
        Landform.Steppe or Landform.RockyDesert or Landform.Badlands => 3,
        Landform.Mesas or Landform.Canyonlands or Landform.Plateau => 2,
        Landform.Hills or Landform.BasaltPlains => 1,
        _ => 0,
    };

    /// <summary>How well a region suits an inland landform, about 0-1.</summary>
    static float Suit(Landform l, in RegionContext c)
    {
        float dry = c.Dry, wet = 1f - c.Dry, low = 1f - c.Elev, cold = Math.Clamp(1f - c.Temp * 2.2f, 0f, 1f), hot = Math.Clamp(c.Temp * 1.6f - 0.6f, 0f, 1f);
        float coast = c.Land > 0 ? c.CoastLand / (float)c.Land : 0f;
        return l switch
        {
            Landform.Plains => 0.55f + 0.35f * low - 0.25f * c.Belt - 0.15f * dry,
            Landform.Steppe => 0.3f + 0.6f * (1f - Math.Abs(dry - 0.65f) * 2f) + 0.2f * c.Elev - 0.3f * coast,
            Landform.Hills => 0.35f + 0.45f * Math.Min(1f, c.Belt * 3f) + 0.25f * c.Elev + 0.15f * wet,
            Landform.Plateau => 0.3f + 0.6f * c.Elev + 0.1f * dry - 0.2f * coast,
            Landform.Mesas => 0.3f + 0.2f * dry + 0.5f * c.Elev,
            Landform.Canyonlands => 0.2f + 0.15f * dry + 0.6f * c.Elev - 0.2f * coast,
            Landform.RiverValleys => 0.2f + 0.7f * c.Flow + 0.2f * wet - 0.2f * c.Belt,
            Landform.DuneSea => 0.05f + 1.4f * dry * dry + 0.15f * low - 0.2f * coast,
            // Stony plains: dry, but raised and near the ranges rather than the lowest, driest basins (the ergs').
            Landform.RockyDesert => 0.2f + 0.45f * dry + 0.3f * Math.Min(1f, c.Belt * 3f) + 0.3f * c.Elev,
            Landform.Badlands => 0.3f + 0.25f * dry + 0.5f * Math.Min(1f, c.Belt * 3f),
            Landform.Wetlands => 0.1f + 0.45f * low + 0.45f * wet + 0.3f * coast + 0.2f * c.Flow,
            Landform.LakeDistrict => 0.25f + 0.35f * cold + 0.3f * wet + 0.15f * low,
            Landform.IcePlain => 0.2f + 0.8f * cold,
            Landform.VolcanicField => 0.3f + 0.35f * hot + 0.25f * (1f - c.Lat) + 0.2f * Math.Min(1f, c.Belt * 3f),
            Landform.BasaltPlains => 0.4f + 0.4f * low,
            Landform.Karst => 0.25f + 0.4f * wet + 0.3f * hot,
            _ => 0.5f, // alien landforms grow anywhere
        };
    }

    /// <summary>
    /// Inland landforms claim regions up to their share of the land, best-suited first, with a pull
    /// towards neighbours already of the same landform. Every landform of the mix gets at least one region.
    /// </summary>
    void AssignInland(LandformMix mix, RegionContext[] ctx, bool belts, float foothillArea)
    {
        int regions = ctx.Length;
        var inland = new List<Landform>();
        var shares = new List<float>();
        for (int i = 0; i < mix.Count; i++)
        {
            Landform l = mix.Landforms[i];
            if (mix.Single || (!LandformInfo.IsCoastal(l) && !(belts && l == Landform.Mountains)))
            {
                inland.Add(l);
                shares.Add(mix.Shares[i]);
            }
        }
        if (inland.Count == 0)
        {
            inland.Add(Landform.Plains);
            shares.Add(1f);
        }
        if (mix.Single)
        {
            Array.Fill(RegionLandform, mix.Dominant);
            return;
        }

        float totalShare = 0f, totalFree = 0f;
        foreach (float s in shares) totalShare += s;
        foreach (RegionContext c in ctx) totalFree += c.Free;
        var quota = new float[inland.Count];
        for (int i = 0; i < inland.Count; i++) quota[i] = shares[i] / totalShare * totalFree;
        // The foothills round the ranges are hill country already.
        int hills = inland.IndexOf(Landform.Hills);
        if (hills >= 0) quota[hills] -= foothillArea;

        // Each landform ranks the regions by how well they suit it, against its own average: a
        // dune sea wants the driest regions even if a plain would score higher there in absolute terms.
        var fit = new float[regions, inland.Count];
        for (int li = 0; li < inland.Count; li++)
        {
            double sum = 0, sum2 = 0;
            int count = 0;
            for (int r = 0; r < regions; r++)
            {
                if (ctx[r].Land == 0) continue;
                float s = Suit(inland[li], ctx[r]);
                fit[r, li] = s;
                sum += s; sum2 += s * s; count++;
            }
            double mean = count > 0 ? sum / count : 0, std = count > 1 ? Math.Sqrt(Math.Max(1e-6, sum2 / count - mean * mean)) : 1;
            for (int r = 0; r < regions; r++) fit[r, li] = (float)((fit[r, li] - mean) / Math.Max(std, 0.05));
        }
        var assigned = new bool[regions];
        var bonus = new float[regions, inland.Count];
        // A pull towards neighbours of the same landform, so landforms form whole countries; capped
        // so it never outweighs how well a region suits a landform.
        void Take(int r, int li)
        {
            assigned[r] = true;
            RegionLandform[r] = inland[li];
            quota[li] -= ctx[r].Free;
            int ri = r % _rnx, rj = r / _rnx;
            for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
                if (di != 0 || dj != 0)
                {
                    ref float b = ref bonus[RegionIndex(ri + di, rj + dj), li];
                    b = Math.Min(0.6f, b + 0.2f);
                }
        }
        float Score(int r, int li) => fit[r, li] + bonus[r, li] + 0.25f * ctx[r].Jitter;

        // Picky landforms claim first: a dune sea must have the driest country and an ice plain the
        // coldest, while plains and hills do well almost anywhere. Each takes its best region
        // first (so every landform appears), then its share, best-suited regions first, pulled
        // towards its own neighbours so it forms whole countries.
        var order = new List<int>();
        for (int li = 0; li < inland.Count; li++) order.Add(li);
        order.Sort((a, b) => Pickiness(inland[b]).CompareTo(Pickiness(inland[a])) != 0
            ? Pickiness(inland[b]).CompareTo(Pickiness(inland[a])) : a.CompareTo(b));
        int Best(int li)
        {
            int best = -1;
            float bestScore = float.MinValue;
            for (int r = 0; r < regions; r++)
            {
                if (assigned[r] || ctx[r].Free < 1f) continue;
                float s = Score(r, li);
                if (s > bestScore) { bestScore = s; best = r; }
            }
            return best;
        }
        foreach (int li in order)
        {
            int best = Best(li);
            if (best >= 0) Take(best, li);
        }
        foreach (int li in order)
        {
            while (quota[li] > 0f)
            {
                int best = Best(li);
                if (best < 0) break;
                Take(best, li);
            }
        }
        // Land regions left over (every quota met) go to whatever suits them best; sea regions take
        // their neighbours' landform, so nothing foreign bleeds onto a coast.
        for (int r = 0; r < regions; r++)
        {
            if (assigned[r] || ctx[r].Land == 0) continue;
            int best = 0;
            float bestScore = float.MinValue;
            for (int li = 0; li < inland.Count; li++)
            {
                float s = Score(r, li);
                if (s > bestScore) { bestScore = s; best = li; }
            }
            Take(r, best);
        }
        for (int round = 0; round < _rnx + _rny; round++)
        {
            bool any = false;
            for (int r = 0; r < regions; r++)
            {
                if (assigned[r]) continue;
                int ri = r % _rnx, rj = r / _rnx;
                for (int d = 0; d < 4 && !assigned[r]; d++)
                {
                    int nb = RegionIndex(ri + (d == 0 ? 1 : d == 1 ? -1 : 0), rj + (d == 2 ? 1 : d == 3 ? -1 : 0));
                    if (!assigned[nb]) continue;
                    RegionLandform[r] = RegionLandform[nb];
                    assigned[r] = true;
                    any = true;
                }
            }
            if (!any) break;
        }
        for (int r = 0; r < regions; r++) if (!assigned[r]) RegionLandform[r] = inland[0];
    }

    /// <summary>Coastal landforms claim coastal regions: fjords only on cold coasts, cliffs on high ones, archipelagos on shallow shelves.</summary>
    void AssignCoasts(LandformMix mix, RegionContext[] ctx)
    {
        int regions = ctx.Length;
        int coastal = 0;
        foreach (RegionContext c in ctx) if (c.CoastLand > 0 || c.Shelf > 0) coastal++;
        foreach (Landform l in new[] { Landform.Fjords, Landform.Archipelago, Landform.CoastalCliffs })
        {
            if (!mix.Has(l)) continue;
            int want = Math.Max(1, (int)Math.Round(coastal * Math.Clamp(l == Landform.Fjords ? mix.Share(l) : 0.15f + mix.Share(l), 0.08f, 0.5f)));
            if (l == Landform.Archipelago && mix.Dominant == Landform.Archipelago) want = coastal;
            var cands = new List<(int r, float s)>();
            for (int r = 0; r < regions; r++)
            {
                RegionContext c = ctx[r];
                if (RegionCoast[r] != 255) continue;
                float s;
                switch (l)
                {
                    case Landform.Fjords:
                        // Cold coasts only: never a fjord where the sea is warm.
                        if (c.CoastLand == 0 || c.Temp > FjordMaxTemperature) continue;
                        s = (FjordMaxTemperature - c.Temp) + 0.5f * c.Belt + 0.3f * c.Elev;
                        break;
                    case Landform.Archipelago:
                        if (c.Shelf < 6) continue;
                        s = c.Shelf / 50f + 0.2f * c.Jitter;
                        break;
                    default:
                        if (c.CoastLand == 0) continue;
                        s = c.Elev + 0.4f * c.Jitter;
                        break;
                }
                cands.Add((r, s));
            }
            cands.Sort((a, b) => b.s.CompareTo(a.s));
            for (int i = 0; i < Math.Min(want, cands.Count); i++) RegionCoast[cands[i].r] = (byte)l;
        }
    }

    /// <summary>Fjords only form on coasts colder than this (the rough temperature the regions were placed by).</summary>
    public const float FjordMaxTemperature = 0.3f;

    void PlaceDeltas(LandformMix mix, bool[] land, int[] down, float[] flow, float[] temp, float[] mountain, List<DeltaFan> deltas)
    {
        // Mouths: land nodes that drain straight into the sea, biggest first.
        var mouths = new List<int>();
        for (int k = 0; k < land.Length; k++)
            if (land[k] && down[k] >= 0 && !land[down[k]]) mouths.Add(k);
        mouths.Sort((a, b) => flow[b].CompareTo(flow[a]));
        int want = Math.Clamp((int)Math.Round(1 + mix.Share(Landform.Delta) * 6 + Width * Height / 600e6), 1, 12);
        foreach (int m in mouths)
        {
            if (deltas.Count >= want) break;
            if (temp[m] < 0.16f || mountain[m] > 0.3f) continue; // frozen or mountain coasts make no deltas
            float radius = Math.Clamp(1100f + 160f * MathF.Sqrt(flow[m]), 1300f, 3000f);
            double mx = X(m % Nx), my = Y(m / Nx);
            bool clash = false;
            foreach (DeltaFan f in deltas)
            {
                double dx = Mathx.WrapDelta(mx - f.ApexX, Width), dy = Mathx.WrapDelta(my - f.ApexY, Height);
                if (dx * dx + dy * dy < Math.Pow((radius + f.Radius) * 1.1, 2)) clash = true;
            }
            if (clash) continue;
            // The fan opens seaward: down the coast-distance slope at the mouth.
            int i = m % Nx, j = m / Nx;
            float gx = _coast[Index(i + 1, j)] - _coast[Index(i - 1, j)], gy = _coast[Index(i, j + 1)] - _coast[Index(i, j - 1)];
            float gl = MathF.Sqrt(gx * gx + gy * gy);
            if (gl < 1e-3f) continue;
            float ax = -gx / gl, ay = -gy / gl;
            // The apex sits inland of the mouth, on the river's line in.
            double apexX = mx - ax * radius * 0.7, apexY = my - ay * radius * 0.7;
            deltas.Add(new DeltaFan(Mathx.WrapX(apexX, Width), Mathx.Wrap(apexY, Height), ax, ay, radius, flow[m], deltas.Count));
        }
    }

    /// <summary>How much of a delta fan a point is in: within its radius of the apex, in the seaward half-plane, near the coast.</summary>
    internal float FanWeight(in DeltaFan f, double x, double y, float coast)
    {
        double dx = Mathx.WrapDelta(x - f.ApexX, Width), dy = Mathx.WrapDelta(y - f.ApexY, Height);
        float r = (float)Math.Sqrt(dx * dx + dy * dy);
        if (r > f.Radius * 1.25f) return 0f;
        float along = r > 1e-3f ? (float)(dx * f.AxisX + dy * f.AxisY) / r : 1f;
        float radial = Mathx.Smooth(f.Radius * 1.25f, f.Radius * 0.85f, r);
        float angular = Mathx.Smooth(-0.35f, 0.15f, along);
        float shore = Mathx.Smooth(-f.Radius * 0.8f, -f.Radius * 0.45f, coast) * Mathx.Smooth(f.Radius * 1.1f, f.Radius * 0.7f, coast);
        return radial * Math.Max(angular, Mathx.Smooth(f.Radius * 0.4f, 0f, r)) * shore;
    }

    /// <summary>Priority-flood drainage on the coarse grid: each node's downstream neighbour and the rain it gathers.</summary>
    int[] Drainage(float[] e, bool[] land, float[] dry, out float[] flow)
    {
        int n = e.Length;
        var down = new int[n];
        var queued = new bool[n];
        var order = new int[n];
        int reached = 0;
        var filled = new float[n];
        var queue = new PriorityQueue<int, float>();
        for (int k = 0; k < n; k++)
        {
            down[k] = -1;
            if (land[k]) continue;
            queued[k] = true;
            filled[k] = 0f;
            queue.Enqueue(k, 0f);
        }
        while (queue.TryDequeue(out int c, out float level))
        {
            if (land[c]) order[reached++] = c;
            int ci = c % Nx, cj = c / Nx;
            for (int d = 0; d < 8; d++)
            {
                int nb = Index(ci + Di[d], cj + Dj[d]);
                if (queued[nb]) continue;
                queued[nb] = true;
                filled[nb] = Math.Max(e[nb], level + 0.01f);
                down[nb] = c;
                queue.Enqueue(nb, filled[nb]);
            }
        }
        flow = new float[n];
        float cellKm2 = (float)(NodeW * NodeH * 1e-6);
        for (int r = 0; r < reached; r++) flow[order[r]] = cellKm2 * Math.Clamp(1f - dry[order[r]] * 0.8f, 0.2f, 1f);
        for (int r = reached - 1; r >= 0; r--)
        {
            int c = order[r];
            if (down[c] >= 0 && land[down[c]]) flow[down[c]] += flow[c];
        }
        return down;
    }

    static readonly int[] Di = { 1, -1, 0, 0, 1, 1, -1, -1 };
    static readonly int[] Dj = { 0, 0, 1, -1, 1, -1, 1, -1 };

    /// <summary>Signed distance in metres from every node to the rough coast (chamfer, run three times so it carries across both seams).</summary>
    float[] SignedCoastDistance(bool[] land)
    {
        int n = land.Length;
        var toSea = Chamfer(land, seaIsZero: true);
        var toLand = Chamfer(land, seaIsZero: false);
        var d = new float[n];
        for (int k = 0; k < n; k++) d[k] = land[k] ? toSea[k] - 0.5f * (float)NodeW : -(toLand[k] - 0.5f * (float)NodeW);
        return d;
    }

    float[] Chamfer(bool[] land, bool seaIsZero)
    {
        int n = land.Length;
        var d = new float[n];
        for (int k = 0; k < n; k++) d[k] = land[k] != seaIsZero ? 0f : 1e9f;
        float a = (float)NodeW, b = (float)Math.Sqrt(NodeW * NodeW + NodeH * NodeH), c = (float)NodeH;
        for (int round = 0; round < 3; round++)
        {
            for (int j = 0; j < Ny; j++)
            for (int i = 0; i < Nx; i++)
            {
                int k = j * Nx + i;
                float v = Math.Min(d[k], d[Index(i - 1, j)] + a);
                v = Math.Min(v, d[Index(i, j - 1)] + c);
                v = Math.Min(v, d[Index(i - 1, j - 1)] + b);
                v = Math.Min(v, d[Index(i + 1, j - 1)] + b);
                d[k] = v;
            }
            for (int j = Ny - 1; j >= 0; j--)
            for (int i = Nx - 1; i >= 0; i--)
            {
                int k = j * Nx + i;
                float v = Math.Min(d[k], d[Index(i + 1, j)] + a);
                v = Math.Min(v, d[Index(i, j + 1)] + c);
                v = Math.Min(v, d[Index(i + 1, j + 1)] + b);
                v = Math.Min(v, d[Index(i - 1, j + 1)] + b);
                d[k] = v;
            }
        }
        return d;
    }

    /// <summary>Each land node's rank (0 lowest, 1 highest) among the land; sea nodes get 0.</summary>
    static float[] Ranks(float[] v, bool[] land)
    {
        var idx = new List<int>();
        for (int k = 0; k < v.Length; k++) if (land[k]) idx.Add(k);
        idx.Sort((a, b) => v[a] != v[b] ? v[a].CompareTo(v[b]) : a.CompareTo(b));
        var r = new float[v.Length];
        for (int t = 0; t < idx.Count; t++) r[idx[t]] = idx.Count > 1 ? t / (float)(idx.Count - 1) : 0.5f;
        return r;
    }

    static float Quantile(float[] values, float q)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        return sorted[Math.Clamp((int)(q * sorted.Length), 0, sorted.Length - 1)];
    }
}
