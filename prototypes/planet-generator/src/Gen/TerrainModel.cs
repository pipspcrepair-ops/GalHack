using System;

namespace Planets.Gen;

/// <summary>
/// The planet's elevation as a pure function of map position (metres east, metres south) and
/// sample spacing. The 32 m world map and the 2 m terrain under the player both call it, so they
/// always agree; the spacing only decides which octaves are fine enough to drop.
///
/// Elevation is built in two halves. Raw() is the large shape (continents, mountains, hills,
/// volcanoes, craters) that the sea-level quantile is taken over. Shape() turns that into the
/// final height above sea level: landforms that only make sense on land (mesas, canyons, dunes,
/// spires, ice shelves), fine detail and the polar caps.
/// </summary>
public sealed class TerrainModel
{
    public readonly PlanetSpec Spec;
    public readonly ArchetypeDef Def;
    public readonly Cylinder Cylinder;
    public readonly double Width, Height;

    /// <summary>Nominal depth of each polar cap, metres. The real edge wobbles around it.</summary>
    public readonly double CapBand;
    public const float CapHeight = 420f;
    public const float CapRamp = 36f;   // the cap wall rises over this many metres: far too steep to climb

    /// <summary>Raw height that ends up at sea level. Set once by the generator from the land-fraction quantile.</summary>
    public float SeaOffset { get; internal set; }

    readonly ulong _seed;
    readonly bool _archipelago;
    readonly Fractal _warpA, _warpB, _continent, _islands, _mountains, _hills, _selector;
    readonly Fractal _detail, _canyon, _canyonWarp, _dune, _spire, _special, _capEdge, _capJag, _rain, _patches;
    readonly FeatureGrid _volcanoGrid, _craterGrid;

    public TerrainModel(PlanetSpec spec, ArchetypeDef def)
    {
        Spec = spec;
        Def = def;
        Width = spec.WidthMeters;
        Height = spec.HeightMeters;
        Cylinder = new Cylinder(Width);
        CapBand = Math.Clamp(Height * 0.07, 220.0, 1600.0);
        _seed = spec.Seed;
        _archipelago = spec.Has(Modifier.Archipelago);

        double continent = Width * def.ContinentScale;
        _warpA = new Fractal(Salt(1), continent * 0.8, 3);
        _warpB = new Fractal(Salt(2), continent * 0.8, 3);
        _continent = new Fractal(Salt(3), continent, 6, 0.5f);
        _islands = new Fractal(Salt(4), continent * 0.3, 5, 0.5f);
        _mountains = new Fractal(Salt(5), def.MountainWavelength, 9, 0.5f);
        _hills = new Fractal(Salt(6), def.HillWavelength, 7, 0.45f);
        _selector = new Fractal(Salt(7), 2600, 3, 0.5f);
        _detail = new Fractal(Salt(8), 72, 6, 0.5f);
        _canyon = new Fractal(Salt(9), def.CanyonWavelength, 3, 0.4f);
        _canyonWarp = new Fractal(Salt(10), def.CanyonWavelength * 0.5, 3, 0.5f);
        _dune = new Fractal(Salt(11), def.DuneWavelength, 3, 0.35f);
        _spire = new Fractal(Salt(12), 520, 5, 0.5f);
        _special = new Fractal(Salt(13), 1400, 4, 0.5f);
        _capEdge = new Fractal(Salt(14), 1800, 5, 0.5f);
        _capJag = new Fractal(Salt(15), 300, 6, 0.5f);
        _rain = new Fractal(Salt(16), 3000, 3, 0.5f);
        _patches = new Fractal(Salt(17), 1100, 3, 0.5f);
        _volcanoGrid = new FeatureGrid(Width, Height, def.VolcanoSpacing, _seed, 21, 0.75f);
        _craterGrid = new FeatureGrid(Width, Height, def.CraterSpacing, _seed, 22, 0.7f);
    }

    ulong Salt(int layer) => Hash.Of(_seed, layer, (long)Spec.Archetype, Spec.Variant);

    // ---- The two halves ----------------------------------------------------------------

    /// <summary>
    /// Large-scale height before sea level is chosen. <paramref name="special"/> returns the
    /// archetype's special field in [0, 1]: volcanic heat, geothermal hotspots, flower meadows...
    /// </summary>
    public float Raw(double x, double y, double spacing, out float special)
    {
        P3 p = Cylinder.At(x, y);

        // Domain warp bends coastlines and mountain chains so nothing lines up.
        double warp = Width * Def.ContinentScale * 0.22;
        float wa = _warpA.Fbm(p, spacing), wb = _warpB.Fbm(p, spacing);
        var q = new P3(p.X + wa * warp, p.Y + wb * warp, p.Z + (wa - wb) * warp * 0.6);

        float c = _continent.Fbm(q, spacing);
        if (_archipelago) c = c * 0.3f + _islands.Fbm(q, spacing) * 0.8f;
        float h = c * Def.ContinentAmp;

        // Mountains rise inland, so coasts are mostly lowland and ranges sit in continental cores.
        float inland = Mathx.Smooth(-0.1f, 0.5f, c);
        float ridges = _mountains.Ridged(q, spacing, Def.RidgeSharpness);
        h += ridges * ridges * Def.MountainHeight * (0.2f + 0.8f * inland);

        float hills = _hills.Fbm(p, spacing);
        if (Def.Billow > 0) hills = Mathx.Lerp(hills, Math.Abs(hills) * 2f - 0.45f, Def.Billow);
        h += hills * Def.HillHeight;

        special = 0f;
        if (Def.VolcanoSpacing > 0) h += Volcanoes(x, y, out special);
        if (Def.CraterSpacing > 0) h += Craters(x, y);
        if (Def.VolcanoSpacing <= 0) special = Mathx.Smooth(0.15f, 0.65f, _special.Fbm(p, spacing));
        return h;
    }

    /// <summary>Final height above sea level from a Raw() value.</summary>
    public float Shape(float raw, double x, double y, double spacing)
    {
        P3 p = Cylinder.At(x, y);
        float e = raw - SeaOffset;

        if (e > 0f)
        {
            float select = _selector.Fbm(p, spacing);   // where terraced landforms win over smooth ones
            if (Def.HighlandLift > 0) e += Def.HighlandLift * Mathx.Smooth(0f, 30f, e);
            if (Def.TerraceStep > 0)
            {
                float cover = Mathx.Smooth(0.35f - Def.TerraceCover, 0.5f - Def.TerraceCover, -select);
                // Step heights drift with position so neighbouring mesas don't share one skyline.
                float offset = (_special.Single(p) * 0.5f + 0.5f) * Def.TerraceStep;
                e = Mathx.Lerp(e, Math.Max(0.5f, Terrace(e + offset, Def.TerraceStep, 0.22f) - offset), cover);
            }
            if (Def.CanyonDepth > 0) e = Canyon(e, p, spacing);
            if (Def.DuneHeight > 0)
            {
                var stretched = new P3(p.X, p.Y * 0.32, p.Z);
                float ridge = 1f - Math.Abs(_dune.Fbm(stretched, spacing));
                // Dune seas cover most open ground and run right down to the shore.
                float field = Mathx.Smooth(-0.5f, -0.15f, select) * Mathx.Smooth(0f, 3f, e);
                e += ridge * ridge * ridge * Def.DuneHeight * field;
            }
            if (Def.SpireHeight > 0)
            {
                float spire = _spire.Ridged(p, spacing, 5f);
                e += spire * spire * Def.SpireHeight * Mathx.Smooth(-0.2f, 0.3f, select) * Mathx.Smooth(0f, 20f, e);
            }
            if (Def.ShelfHeight > 0) e += Def.ShelfHeight * Mathx.Smooth(0f, 7f, e);
        }

        e += _detail.Fbm(p, spacing) * Def.Roughness * (e > 0f ? 1f : 0.4f);

        float cap = CapDepth(x, y);
        if (cap > -1f)
        {
            float t = Mathx.Smooth(0f, CapRamp, cap);
            float top = Math.Max(e, 0f) + CapHeight + CapRelief(p, spacing, cap);
            e = Mathx.Lerp(e, top, t);
        }
        return e;
    }

    public float Elevation(double x, double y, double spacing) => Shape(Raw(x, y, spacing, out _), x, y, spacing);

    // ---- Polar caps --------------------------------------------------------------------

    /// <summary>Metres inside the nearest polar cap; negative outside it.</summary>
    public float CapDepth(double x, double y)
    {
        // The wobble never takes an edge past 1.6 bands, so most of the map skips the noise.
        double margin = CapBand * 1.6;
        if (y > margin && y < Height - margin) return (float)-Math.Min(y - margin, Height - margin - y) - 1f;
        return (float)Math.Max(NorthEdge(x) - y, y - SouthEdge(x));
    }

    /// <summary>Where the north cap begins at this x, metres from the north edge of the map.</summary>
    public double NorthEdge(double x) => CapBand * (1 + 0.35 * _capEdge.Fbm(Cylinder.At(x, 0), 8));

    /// <summary>Where the south cap begins at this x, metres from the north edge of the map.</summary>
    public double SouthEdge(double x) => Height - CapBand * (1 + 0.35 * _capEdge.Fbm(Cylinder.At(x, 5000), 8));

    float CapRelief(in P3 p, double spacing, float depth)
    {
        float jag = _capJag.Ridged(p, spacing, 2.5f);
        if (Def.Cap == CapStyle.IceCliff)
        {
            // Ice cliffs: a sheer wall, then stepped tiers of ice behind it.
            return Terrace(depth * 0.35f + jag * 40f, 60f, 0.15f);
        }
        return jag * 220f + depth * 0.25f; // storm walls hide a jagged range that keeps climbing
    }

    // ---- Landforms ---------------------------------------------------------------------

    /// <summary>Flat treads with steep risers; continuous, so it can be blended in and out.</summary>
    static float Terrace(float h, float step, float riser)
    {
        float k = h / step;
        float floor = MathF.Floor(k);
        return (floor + Mathx.Smooth(1f - riser, 1f, k - floor)) * step;
    }

    float Canyon(float e, in P3 p, double spacing)
    {
        double warp = Def.CanyonWavelength * 0.35;
        float w = _canyonWarp.Fbm(p, spacing);
        var q = new P3(p.X + w * warp, p.Y - w * warp, p.Z + w * warp * 0.5);
        // Canyons follow the zero line of a noise field: long, winding and connected.
        float line = Math.Abs(_canyon.Fbm(q, spacing));
        const float width = 0.075f;
        float inside = 1f - Mathx.Smooth(width * 0.45f, width, line);
        float floor = Math.Max(2.5f, e * 0.12f);
        float depth = Math.Min(Def.CanyonDepth, e - floor);
        return depth > 0 ? e - depth * inside * Mathx.Smooth(8f, 50f, e) : e;
    }

    /// <summary>Volcano cones with calderas on a jittered grid that wraps east-west.</summary>
    float Volcanoes(double x, double y, out float heat)
    {
        float result = 0f;
        heat = 0f;
        _volcanoGrid.Cell(x, y, out int ix, out int iy);
        for (int cj = -1; cj <= 1; cj++)
        for (int ci = -1; ci <= 1; ci++)
        {
            if (!_volcanoGrid.Feature(ix + ci, iy + cj, out double fx, out double fy, out ulong h)) continue;
            float radius = Def.VolcanoSpacing * Mathx.Lerp(0.32f, 0.55f, Hash.Unit(h, 3));
            float dx = (float)Mathx.WrapDelta(x - fx, Width), dy = (float)(y - fy);
            float angle = MathF.Atan2(dy, dx), phase = Hash.Unit(h, 5) * 6.2832f;
            // Lobed outline (old flows ran further on some sides) and radial gullies on the
            // flanks: together they make a cone read as a volcano rather than a dome.
            float lobes = 1f + 0.16f * MathF.Sin(angle * 3f + phase) + 0.08f * MathF.Sin(angle * 5f - phase * 1.7f);
            float t = MathF.Sqrt(dx * dx + dy * dy) / (radius * lobes);
            if (t >= 1.4f) continue;
            float height = Def.VolcanoHeight * Mathx.Lerp(0.45f, 1.15f, Hash.Unit(h, 4));
            float cone = t < 1f ? height * MathF.Pow(1f - t, 1.7f) : 0f;
            float gully = 0.5f + 0.5f * MathF.Sin(angle * 18f + phase + t * 5f);
            cone *= 1f - 0.12f * gully * Mathx.Smooth(0.1f, 0.4f, t) * Mathx.Smooth(1f, 0.55f, t);
            if (t < 0.16f) cone -= height * 0.22f * (1f - t * t / (0.16f * 0.16f)); // caldera
            result = Math.Max(result, cone);
            heat = Math.Max(heat, Mathx.Smooth(0.75f, 0f, t));
        }
        return result;
    }

    /// <summary>Bowl craters with raised rims, summed so young craters overprint old ones.</summary>
    float Craters(double x, double y)
    {
        float result = 0f;
        _craterGrid.Cell(x, y, out int ix, out int iy);
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (!_craterGrid.Feature(ix + dx, iy + dy, out double fx, out double fy, out ulong h)) continue;
            float radius = Def.CraterSpacing * Mathx.Lerp(0.1f, 0.42f, MathF.Pow(Hash.Unit(h, 3), 2f));
            float t = (float)Distance(x, y, fx, fy) / radius;
            if (t >= 1.8f) continue;
            float bowl = t < 1f ? (t * t - 1f) * radius * 0.24f : 0f;
            float rim = radius * 0.09f * MathF.Exp(-(t - 1f) * (t - 1f) / 0.05f);
            result += bowl + rim;
        }
        return result;
    }

    /// <summary>
    /// Feature points on a jittered grid, at most one per cell. The grid has a whole number of
    /// cells around the planet, so features wrap east-west like everything else.
    /// </summary>
    readonly struct FeatureGrid
    {
        readonly int _nx, _ny, _salt;
        readonly double _width, _cw, _ch;
        readonly float _presence;
        readonly ulong _seed;

        public FeatureGrid(double width, double height, double spacing, ulong seed, int salt, float presence)
        {
            spacing = Math.Max(spacing, 1);
            _nx = Math.Max(1, (int)Math.Round(width / spacing));
            _ny = Math.Max(1, (int)Math.Round(height / spacing));
            _width = width;
            _cw = width / _nx;
            _ch = height / _ny;
            _seed = seed;
            _salt = salt;
            _presence = presence;
        }

        public void Cell(double x, double y, out int ix, out int iy)
        {
            ix = (int)Math.Floor(Mathx.WrapX(x, _width) / _cw);
            iy = (int)Math.Floor(y / _ch);
        }

        public bool Feature(int ix, int iy, out double fx, out double fy, out ulong hash)
        {
            fx = fy = 0;
            hash = 0;
            if (iy < 0 || iy >= _ny) return false;
            ix = (ix % _nx + _nx) % _nx;
            hash = Hash.Of(_seed, ix, iy, _salt);
            if (Hash.Unit(hash, 0) > _presence) return false;
            fx = (ix + 0.15 + 0.7 * Hash.Unit(hash, 1)) * _cw;
            fy = (iy + 0.15 + 0.7 * Hash.Unit(hash, 2)) * _ch;
            return true;
        }
    }
    double Distance(double x, double y, double fx, double fy)
    {
        double dx = Mathx.WrapDelta(x - fx, Width), dy = y - fy;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Rainfall weight in [0.4, 1.6], for river catchments.</summary>
    public float Rain(double x, double y) => 1f + 0.6f * Math.Clamp(_rain.Fbm(Cylinder.At(x, y), 64), -1f, 1f);

    /// <summary>Patch noise in roughly [-1, 1] for biomes that come in scattered patches (sulfur flats).</summary>
    public float Patches(double x, double y) => _patches.Fbm(Cylinder.At(x, y), 64);

    /// <summary>Weather noise, roughly in [-1, 1], that breaks up latitude bands and humidity rings.</summary>
    public void ClimateNoise(double x, double y, out float temperature, out float humidity)
    {
        P3 p = Cylinder.At(x, y);
        temperature = _rain.Fbm(new P3(p.Z, p.Y + 7000, p.X), 64);
        humidity = _special.Fbm(new P3(p.Y - 9000, p.X, p.Z), 64);
    }
}
