using System;

namespace Planets.Gen;

/// <summary>
/// The planet's elevation as a pure function of map position (metres east, metres south) and
/// sample spacing. The 32 m world map and the 2 m terrain under the player both call it, so they
/// always agree; the spacing only decides which octaves are fine enough to drop.
///
/// The map is a torus: it wraps east-west and north-south. Every noise field is sampled on a
/// flat 4D torus (see <see cref="Torus"/>), and every grid (volcanoes, cones, rings, craters, the
/// landform layer) wraps on both axes, so heights join perfectly across both seams and at all
/// four corners.
///
/// The land is divided into landform regions (<see cref="Landforms"/>): plains, mountains, dune
/// seas, deltas and the rest (LANDFORMS.md). Each sample blends the recipes of the landforms
/// that hold there (<see cref="LandformRelief"/>) and runs one shared relief engine with the
/// blend, so a border is a gradient from one landform to the next, never a seam.
///
/// Elevation is built in two halves. Raw() is the large shape before sea level exists: the
/// continents, islands on archipelago shelves, coastline detail, big volcanoes and craters. The
/// sea-level quantile is taken over it. Shape() turns that into the final height above sea
/// level, landform by landform: flattening, lifts, hills, ranges, tablelands, mesas, canyons,
/// dunes, gullies, towers, spires, cones, rings, lakes, fjords, cliffs and deltas, then fine
/// detail. Working in metres above sea level means a delta sits at the waterline and a fjord's
/// floor below it, wherever the coast falls.
/// </summary>
public sealed class TerrainModel
{
    public readonly PlanetSpec Spec;
    public readonly ArchetypeDef Def;
    public readonly Torus Torus;
    public readonly double Width, Height;

    /// <summary>Raw height that ends up at sea level. Set once by the generator from the land-fraction quantile.</summary>
    public float SeaOffset { get; internal set; }

    /// <summary>Which landform holds where, and how strongly (the region layer).</summary>
    public LandformLayer Landforms { get; }

    /// <summary>This world's landforms and their shares.</summary>
    public LandformMix Mix => Landforms.Mix;

    const double LowSpacing = 450;     // the flattened base keeps only wavelengths over ~2 km
    const float CoastDetail = 16f;     // metres of coastline wiggle, before sea level

    readonly ulong _seed;
    readonly bool _archipelago;
    readonly Fractal _warpA, _warpB, _continent, _islands, _mountains, _hills, _selector;
    readonly Fractal _detail, _canyon, _canyonWarp, _dune, _spire, _special, _rain, _patches;
    readonly Fractal _ranges, _plateau, _plateauWarp, _jag, _region, _meander;
    readonly Fractal _swell, _badland, _tower, _lakes, _fjord, _fjordWarp, _coast, _duneField, _duneWander;
    readonly Noise _deltaNoise;
    readonly FeatureGrid _volcanoGrid, _craterGrid, _coneGrid, _ringGrid;
    readonly float[] _volcanoPresence, _conePresence, _ringHeight;
    readonly int _duneA, _duneB;
    readonly double _duneWave;
    readonly ChannelMap[] _deltaChannels;

    /// <summary>One straight piece of a delta's distributary, in the fan's own frame (metres along its axis and across it).</summary>
    readonly record struct Channel(float A0, float C0, float A1, float C1, float Width);

    /// <summary>A delta's channels and, per square of a grid over its fan, which of them pass near it: a fan has thousands of short pieces.</summary>
    readonly record struct ChannelMap(Channel[] Channels, int[][] Near, int Size, float Extent);
    const float ChannelCell = 48f, ChannelReach = 22f; // the grid's squares, and how far from a channel's edge its banks and their wobble can reach

    public TerrainModel(PlanetSpec spec, ArchetypeDef def)
    {
        Spec = spec;
        Def = def;
        Width = spec.WidthMeters;
        Height = spec.HeightMeters;
        Torus = new Torus(Width, Height);
        _seed = spec.Seed;

        double continent = Width * def.ContinentScale;
        _warpA = new Fractal(Salt(1), continent * 0.8, 3);
        _warpB = new Fractal(Salt(2), continent * 0.8, 3);
        _continent = new Fractal(Salt(3), continent, 6, 0.5f);
        _islands = new Fractal(Salt(4), continent * 0.3, 5, 0.5f);
        _mountains = new Fractal(Salt(5), def.MountainWavelength * 0.7, def.MountainOctaves, 0.5f);
        _hills = new Fractal(Salt(6), def.HillWavelength, 7, 0.45f);
        _selector = new Fractal(Salt(7), 2600, 3, 0.5f);
        _detail = new Fractal(Salt(8), 72, 6, 0.5f);
        _canyon = new Fractal(Salt(9), def.CanyonWavelength, 3, 0.4f);
        _canyonWarp = new Fractal(Salt(10), def.CanyonWavelength * 0.5, 3, 0.5f);
        _dune = new Fractal(Salt(11), 1100, 2, 0.5f);
        _spire = new Fractal(Salt(12), 520, 5, 0.5f);
        _special = new Fractal(Salt(13), 1400, 4, 0.5f);
        _rain = new Fractal(Salt(16), 3000, 3, 0.5f);
        _patches = new Fractal(Salt(17), 1100, 3, 0.5f);
        _ranges = new Fractal(Salt(18), def.MountainWavelength * 2.2, 3, 0.5f);
        _plateau = new Fractal(Salt(19), def.PlateauWavelength, 4, 0.55f);
        _plateauWarp = new Fractal(Salt(20), def.PlateauWavelength * 0.6, 2, 0.5f);
        _jag = new Fractal(Salt(21), 260, 5, 0.6f);
        _region = new Fractal(Salt(22), 1400, 2, 0.5f);
        _meander = new Fractal(Salt(23), 380, 2, 0.45f);
        _swell = new Fractal(Salt(24), 2200, 3, 0.5f);
        _badland = new Fractal(Salt(25), 170, 4, 0.5f);
        _tower = new Fractal(Salt(26), 300, 3, 0.45f);
        _lakes = new Fractal(Salt(27), 560, 3, 0.5f);
        _fjord = new Fractal(Salt(28), 2600, 3, 0.45f);
        _fjordWarp = new Fractal(Salt(29), 1500, 2, 0.5f);
        _coast = new Fractal(Salt(30), 700, 4, 0.5f);
        _duneField = new Fractal(Salt(31), 1300, 2, 0.5f);
        _duneWander = new Fractal(Salt(33), 420, 2, 0.5f);
        _deltaNoise = new Noise(Salt(32));
        _volcanoGrid = new FeatureGrid(Width, Height, def.VolcanoSpacing, _seed, 21, 0.75f);
        _craterGrid = new FeatureGrid(Width, Height, def.CraterSpacing, _seed, 22, 0.7f);
        _coneGrid = new FeatureGrid(Width, Height, 900, _seed, 23, 0.8f);
        _ringGrid = new FeatureGrid(Width, Height, 2300, _seed, 24, 0.8f);

        // The landform layer: placed on the continents' large shape, before any landform relief.
        LandformMix mix = LandformMix.Roll(spec, def);
        _archipelago = spec.Has(Modifier.Archipelago) || (mix.Single && mix.Dominant == Landform.Archipelago);
        Landforms = new LandformLayer(this, mix, (x, y, s) => Continent(Torus.At(x, y), s, out _) * Def.ContinentAmp,
            PlanetGenerator.RollLandTarget(spec, def));

        // Point features take the landform where they stand: a cone field's cones, a ringland's rings.
        _volcanoPresence = def.VolcanoSpacing > 0 ? Presence(_volcanoGrid, r => r.Volcano) : Array.Empty<float>();
        _conePresence = Presence(_coneGrid, r => r.Cone);
        _ringHeight = Presence(_ringGrid, r => r.Ring);

        // Dunes lie square to the prevailing wind, as a wave with a whole number of crests round
        // the planet both ways, so the dune field wraps like everything else.
        _duneWave = def.DuneWavelength > 0 ? def.DuneWavelength : 160;
        _duneA = (int)Math.Round(Landforms.WindX * Width / _duneWave);
        _duneB = (int)Math.Round(Landforms.WindY * Height / _duneWave);
        if (_duneA == 0 && _duneB == 0) _duneA = 1;

        _deltaChannels = new ChannelMap[Landforms.Deltas.Count];
        for (int d = 0; d < _deltaChannels.Length; d++) _deltaChannels[d] = Distributaries(Landforms.Deltas[d]);
    }

    /// <summary>
    /// A delta's channels. The river comes in at the apex and runs out to the sea, shedding a side
    /// channel every so often, off one bank or the other at no set angle: each takes a share of the
    /// water and sheds its own, and some die out in the marsh before they reach the sea. Each
    /// heads away from a point behind the apex, so neighbours draw apart and the fan opens as it
    /// goes, and swings in bends that grow downstream, flaring where it meets the sea.
    /// </summary>
    ChannelMap Distributaries(DeltaFan f)
    {
        const float Step = 16f;
        var list = new System.Collections.Generic.List<Channel>();
        var rng = new Rng(Hash.Of(_seed, f.Id, 0x44454C54));
        float reach = f.Radius * 1.3f, behind = f.Radius * 0.35f;
        void Grow(float a, float c, float heading, float width, int depth, float life)
        {
            float wave = rng.Range(200f, 340f), phase = rng.Range(0f, MathF.Tau), next = f.Radius * rng.Range(0.12f, 0.28f), walked = 0f;
            while (walked < life)
            {
                float r = MathF.Sqrt(a * a + c * c) / f.Radius;
                heading += (MathF.Atan2(c, a + behind) - heading) * 0.05f + rng.Range(-0.05f, 0.05f);
                float swing = walked / wave * MathF.Tau + phase;
                float bend = Mathx.Lerp(0.3f, 1.05f, Math.Clamp(r, 0f, 1f)) * (MathF.Sin(swing) + 0.3f * MathF.Sin(swing * 2.3f + phase));
                float angle = heading + bend;
                float na = a + MathF.Cos(angle) * Step, nc = c + MathF.Sin(angle) * Step;
                list.Add(new Channel(a, c, na, nc, width * (1f + 0.9f * Mathx.Smooth(0.85f, 1.25f, r))));
                a = na;
                c = nc;
                walked += Step;
                if (a * a + c * c > reach * reach) return;
                if (walked >= next && depth < 4 && width > 8f)
                {
                    float side = rng.NextFloat() < 0.5f ? -1f : 1f;
                    bool dies = rng.NextFloat() < 0.3f;
                    Grow(a, c, angle + side * rng.Range(0.5f, 1.1f), width * rng.Range(0.42f, 0.68f), depth + 1, dies ? f.Radius * rng.Range(0.2f, 0.45f) : float.MaxValue);
                    width *= rng.Range(0.76f, 0.92f);
                    heading -= side * rng.Range(0.08f, 0.28f); // the main channel leans the other way
                    next = walked + f.Radius * rng.Range(0.12f, 0.28f);
                }
            }
        }
        Grow(-f.Radius * 0.1f, 0f, 0f, Math.Clamp(f.Radius * 0.028f, 30f, 70f), 0, float.MaxValue);

        float extent = reach + 2f * Step + 200f;
        int size = (int)Math.Ceiling(2f * extent / ChannelCell);
        var near = new System.Collections.Generic.List<int>[size * size];
        for (int k = 0; k < list.Count; k++)
        {
            Channel c = list[k];
            float margin = c.Width * 0.5f + ChannelReach;
            int a0 = (int)((Math.Min(c.A0, c.A1) - margin + extent) / ChannelCell), a1 = (int)((Math.Max(c.A0, c.A1) + margin + extent) / ChannelCell);
            int c0 = (int)((Math.Min(c.C0, c.C1) - margin + extent) / ChannelCell), c1 = (int)((Math.Max(c.C0, c.C1) + margin + extent) / ChannelCell);
            for (int j = Math.Max(c0, 0); j <= Math.Min(c1, size - 1); j++)
            for (int i = Math.Max(a0, 0); i <= Math.Min(a1, size - 1); i++)
                (near[j * size + i] ??= new()).Add(k);
        }
        var cells = new int[size * size][];
        for (int k = 0; k < cells.Length; k++) cells[k] = near[k]?.ToArray() ?? Array.Empty<int>();
        return new ChannelMap(list.ToArray(), cells, size, extent);
    }

    ulong Salt(int layer) => Hash.Of(_seed, layer, (long)Spec.Archetype, Spec.Variant);

    float[] Presence(FeatureGrid grid, Func<LandformRelief, float> pick)
    {
        var result = new float[grid.Cells];
        for (int c = 0; c < grid.Cells; c++)
        {
            if (!grid.Feature(c % grid.Nx, c / grid.Nx, out double fx, out double fy, out _)) continue;
            double x = Mathx.WrapX(fx, Width), y = Mathx.Wrap(fy, Height);
            result[c] = pick(Landforms.Relief(Landforms.At(x, y)));
        }
        return result;
    }

    // ---- The two halves ----------------------------------------------------------------

    /// <summary>
    /// Large-scale height before sea level is chosen. <paramref name="special"/> returns the
    /// archetype's special field in [0, 1]: volcanic heat, geothermal hotspots, flower meadows...
    /// </summary>
    public float Raw(double x, double y, double spacing, out float special)
    {
        P4 p = Torus.At(x, y);
        Landforms.Sample(x, y, p, out LandformBlend blend, out _, out _);
        LandformRelief r = Landforms.Relief(blend);
        return RawWith(x, y, spacing, p, r, out special);
    }

    /// <summary>The continent field (noise units, about -1..1) at a point, and the warped point it was read at.</summary>
    float Continent(in P4 p, double spacing, out P4 q)
    {
        // Domain warp bends coastlines and continents so nothing lines up.
        double warp = Width * Def.ContinentScale * 0.22;
        float wa = _warpA.Fbm(p, spacing), wb = _warpB.Fbm(p, spacing);
        q = new P4(p.X + wa * warp, p.Y + wb * warp, p.Z + (wa - wb) * warp * 0.6, p.W + (wa + wb) * warp * 0.5);
        float c = _continent.Fbm(q, spacing);
        if (_archipelago) c = c * 0.3f + _islands.Fbm(q, spacing) * 0.8f;
        return c;
    }

    float RawWith(double x, double y, double spacing, in P4 p, in LandformRelief r, out float special)
    {
        float c = Continent(p, spacing, out P4 q);
        // Archipelagos: islands raised out of the shallow shelf, broken coasts behind them.
        if (!_archipelago && r.Islands > 0.01f) c = Mathx.Lerp(c, c * 0.3f + _islands.Fbm(q, spacing) * 0.8f, Math.Min(1f, r.Islands));
        float h = c * Def.ContinentAmp + _coast.Fbm(p, spacing) * CoastDetail;

        special = 0f;
        if (Def.VolcanoSpacing > 0) h += Volcanoes(x, y, out special);
        if (Def.CraterSpacing > 0) h += Craters(x, y);
        if (Def.VolcanoSpacing <= 0) special = Mathx.Smooth(0.15f, 0.65f, _special.Fbm(p, spacing));
        return h;
    }

    /// <summary>Final height above sea level from a Raw() value.</summary>
    public float Shape(float raw, double x, double y, double spacing)
    {
        P4 p = Torus.At(x, y);
        Landforms.Sample(x, y, p, out LandformBlend blend, out float coast, out _, out float level);
        LandformRelief r = Landforms.Relief(blend);
        return ShapeWith(raw, x, y, spacing, p, r, coast, level);
    }

    public float Elevation(double x, double y, double spacing) => Elevation(x, y, spacing, out _);

    /// <summary>Elevation, and how far rivers here wander off their course (a multiplier).</summary>
    internal float Elevation(double x, double y, double spacing, out float meander)
    {
        P4 p = Torus.At(x, y);
        Landforms.Sample(x, y, p, out LandformBlend blend, out float coast, out _, out float level);
        LandformRelief r = Landforms.Relief(blend);
        meander = r.Meander;
        return ShapeWith(RawWith(x, y, spacing, p, r, out _), x, y, spacing, p, r, coast, level);
    }

    /// <summary>For the world map: the final height, the dominant landform, and its river, rain and dryness numbers.</summary>
    internal float ShapeCell(float raw, double x, double y, double spacing, out Landform dominant, out float riverArea, out float rain, out float dry)
    {
        P4 p = Torus.At(x, y);
        Landforms.Sample(x, y, p, out LandformBlend blend, out float coast, out float contextDry, out float level);
        LandformRelief r = Landforms.Relief(blend);
        dominant = blend.Dominant;
        riverArea = r.RiverArea;
        rain = r.Rain;
        dry = contextDry + r.Dry;
        return ShapeWith(raw, x, y, spacing, p, r, coast, level);
    }

    float ShapeWith(float raw, double x, double y, double spacing, in P4 p, in LandformRelief r, float coast, float baseLevel)
    {
        float e = raw - SeaOffset;

        if (e > 0f)
        {
            if (Def.HighlandLift > 0) e += Def.HighlandLift * Mathx.Smooth(0f, 30f, e);

            // The base surface. Open ground keeps only the continent's broad shape (nothing under
            // about 2 km), so plains are flat to gently rolling for kilometres; the coastline
            // itself stays where it is.
            if (r.Flatten > 0.01f)
            {
                float lift = Def.HighlandLift > 0 ? Def.HighlandLift : 0f;
                float low = BaseLow(p) - SeaOffset + lift;
                // Compressing squeezes what is left round the country's own level (the continent
                // averaged over a couple of kilometres), so a plain stays at its height and stays flat.
                float level = baseLevel - SeaOffset + lift;
                low = level + (low - level) * (1f - Math.Clamp(r.Compress, 0f, 0.95f));
                e = Mathx.Lerp(e, Math.Max(low, 2.6f), Math.Min(1f, r.Flatten) * Mathx.Smooth(0f, 14f, e));
            }
            // Wetlands and deltas: squeezed down to a metre or two above the water.
            if (r.Squash > 0.01f) e = Mathx.Lerp(e, 1.9f + e * 0.04f, Math.Min(1f, r.Squash) * Mathx.Smooth(0f, 2f, e));
            float inland = Mathx.Smooth(0f, 25f, e);
            if (r.Lift > 0.1f || r.Swell > 0.1f)
                e += (r.Lift + r.Swell * (0.5f + 0.5f * _swell.Fbm(p, spacing))) * inland;

            // Sea cliffs: the land steps straight up from the shore, broken here and there by a cove.
            if (r.Cliff > 0.5f)
            {
                float cove = Mathx.Smooth(-0.5f, -0.2f, _region.Fbm(p.Swap, spacing));
                float jag = _jag.Fbm(p, spacing) * 1.5f;
                e += r.Cliff * cove * Mathx.Smooth(0.6f + jag, 3.5f + jag, e);
            }

            if (r.Hill > 0.5f)
            {
                float hills = _hills.Fbm(p, spacing);
                if (r.Billow > 0.01f) hills = Mathx.Lerp(hills, Math.Abs(hills) * 2f - 0.45f, Math.Min(1f, r.Billow));
                float rise = 0.5f + 0.5f * hills;
                // Valley floors: the low ground between hills is flat, where the rivers run.
                if (r.ValleyFloor > 0.01f) rise = SoftMax(rise, r.ValleyFloor * 0.42f, 0.06f);
                e += rise * r.Hill * inland;
            }

            // Ranges: a broad massif carved into crests, spurs and valleys. On fjord coasts the
            // mountains rise straight from the shore.
            if (r.Mountain + r.Massif > 1f)
            {
                float steep = Math.Clamp(r.Fjord * 1.5f, 0f, 1f);
                float rise = Mathx.Lerp(Mathx.Smooth(0f, 40f, e), Mathx.Smooth(0f, 5f, e), steep);
                float ridges = _mountains.Ridged(p, spacing, Math.Max(1f, r.Sharpness));
                // Heights grow with the square of how much the range holds, so its edges grade
                // down into foothills instead of standing straight up out of the plain.
                float hold = Math.Clamp(r.Mountain / Math.Max(1f, Def.MountainHeight), 0f, 1f);
                e += (r.Massif + r.Mountain * ridges * ridges) * rise * Mathx.Lerp(0.15f, 1f, hold);
            }

            if (r.Plateau > 0.5f && r.PlateauOn > 0.01f) e += Plateaus(p, spacing, e, r.Plateau, r.PlateauCover / r.PlateauOn);

            if (r.Terrace > 0.01f && r.TerraceOn > 0.01f)
            {
                float step = r.TerraceStep / r.TerraceOn, cover = Math.Min(1f, r.Terrace / r.TerraceOn);
                float select = _selector.Fbm(p, spacing);
                float where = Mathx.Smooth(0.35f - cover, 0.5f - cover, -select) * Math.Min(1f, r.TerraceOn);
                // A terraced mountain reads as a ziggurat, so steps fade out up the flanks of real peaks.
                where *= Mathx.Smooth(step * 9f, step * 6f, e);
                // Step heights drift with position so neighbouring mesas don't share one skyline,
                // and a jagged term breaks each step's edge into spurs and gullies.
                float offset = (_special.Single(p) * 0.5f + 0.5f + _jag.Fbm(p, spacing) * 0.45f) * step;
                e = Mathx.Lerp(e, Math.Max(0.5f, Terrace(e + offset, step, 0.22f) - offset), where);
            }

            if (r.Canyon > 1f && r.CanyonOn > 0.01f) e = Canyon(e, p, spacing, r.Canyon, r.CanyonWidth / r.CanyonOn, r.CanyonFlat / r.CanyonOn);

            if (r.Dune > 0.5f) e += Dunes(p, x, y, spacing, r.Dune) * Mathx.Smooth(0f, 3f, e);

            if (r.Badland > 0.5f)
            {
                float g = _badland.Ridged(p, spacing, 1.7f);
                e += (g - 0.3f) * r.Badland * inland;
            }

            if (r.Tower > 1f && r.TowerOn > 0.01f)
            {
                // Inselbergs (sparse) and karst towers (dense): steep walls, rounded tops.
                float th = 0.6f - 1.3f * (r.TowerCover / r.TowerOn);
                float n = _tower.Fbm(p, spacing);
                float t = Mathx.Smooth(th, th + 0.045f, n) * (0.78f + 0.22f * Mathx.Smooth(th + 0.05f, th + 0.35f, n));
                e += t * r.Tower * inland;
            }

            if (r.Spire > 1f)
            {
                float select = _selector.Fbm(p, spacing);
                float spire = _spire.Ridged(p, spacing, 5f);
                e += spire * spire * r.Spire * Mathx.Smooth(-0.55f, 0.1f, select) * Mathx.Smooth(0f, 20f, e);
            }

            if (r.Cone > 0.01f) e += Cones(x, y) * Mathx.Smooth(0f, 6f, e);
            if (r.Ring > 0.5f) e += Rings(x, y, spacing) * inland;

            if (r.Lake > 0.2f && r.LakeOn > 0.01f)
            {
                float on = Math.Min(1f, r.LakeOn);
                float th = 0.6f - 1.3f * (r.LakeCover / r.LakeOn);
                float basin = Mathx.Smooth(th, th + 0.2f, _lakes.Fbm(p, spacing));
                e = Mathx.Lerp(e, -r.Lake / r.LakeOn, basin * on);
            }
        }

        // Fjords: U-shaped troughs whose floors lie below the sea near the coast and climb inland.
        if (r.Fjord > 0.01f && e > -60f)
        {
            double warp = 650;
            float w = _fjordWarp.Fbm(p, spacing);
            var q = new P4(p.X + w * warp, p.Y - w * warp, p.Z + w * warp * 0.6, p.W - w * warp * 0.5);
            float line = Math.Abs(_fjord.Fbm(q, spacing)) + _jag.Fbm(q, spacing) * 0.008f;
            const float TroughWidth = 0.085f;
            float inside = 1f - Mathx.Smooth(TroughWidth * 0.5f, TroughWidth, line);
            float floor = -42f + 90f * Mathx.Smooth(-400f, 4500f, coast);
            e = Mathx.Lerp(e, Math.Min(e, floor), inside * Math.Min(1f, r.Fjord * 1.4f));
        }

        if (r.Delta > 0.01f) e = Delta(e, x, y, r.Delta);

        e += _detail.Fbm(p, spacing) * r.Roughness * (e > 0f ? 1f : 0.4f);
        return e;
    }

    /// <summary>The continent's broad shape alone (wavelengths over about 2 km), as raw height.</summary>
    float BaseLow(in P4 p) => Continent(p, LowSpacing, out _) * Def.ContinentAmp;

    // ---- Landforms ---------------------------------------------------------------------

    /// <summary>Flat treads with steep risers; continuous, so it can be blended in and out.</summary>
    static float Terrace(float h, float step, float riser)
    {
        float k = h / step;
        float floor = MathF.Floor(k);
        return (floor + Mathx.Smooth(1f - riser, 1f, k - floor)) * step;
    }

    /// <summary>A smooth maximum: like Math.Max, but rounded over <paramref name="k"/>.</summary>
    static float SoftMax(float a, float b, float k)
    {
        float h = Math.Clamp(0.5f + 0.5f * (a - b) / k, 0f, 1f);
        return Mathx.Lerp(b, a, h) + k * h * (1f - h);
    }

    float Canyon(float e, in P4 p, double spacing, float depthMax, float width0, float flat)
    {
        double warp = Def.CanyonWavelength * 0.35;
        float w = _canyonWarp.Fbm(p, spacing);
        var q = new P4(p.X + w * warp, p.Y - w * warp, p.Z + w * warp * 0.5, p.W - w * warp * 0.7);
        // Canyons follow the zero line of a noise field: long, winding and connected. Width and
        // depth wander along them and their walls are ragged.
        float region = _region.Fbm(p, spacing);
        float width = width0 * (0.55f + 0.9f * Mathx.Smooth(-0.45f, 0.45f, _region.Fbm(p.Swap, spacing)));
        float line = Math.Abs(_canyon.Fbm(q, spacing)) + _jag.Fbm(q, spacing) * width * 0.45f;
        // A gorge: V-shaped, steepest near the bottom, opening out toward the rim.
        float t = 1f - Mathx.Smooth(0f, width, line);
        float gorge = t * t * (2f - t);
        // A flat floor between steep walls; canyon country steps its walls in ledges, like rock
        // strata weathering back.
        float floorWall = 1f - Mathx.Smooth(width * 0.4f, width, line);
        if (flat > 0.85f) floorWall = Terrace(floorWall, 1f / Math.Max(2, Def.CanyonSteps > 0 ? Def.CanyonSteps : 3), 0.45f);
        float inside = Mathx.Lerp(gorge, floorWall, Math.Clamp(flat, 0f, 1f));
        float floor = Math.Max(2.5f, e * 0.12f);
        float depth = Math.Min(depthMax * (0.6f + 0.4f * Mathx.Smooth(-0.3f, 0.3f, region)), e - floor);
        return depth > 0 ? e - depth * inside * Mathx.Smooth(8f, 50f, e) : e;
    }

    /// <summary>
    /// Plateaus, tablelands and escarpments: ground lifted where a slow noise field crosses a
    /// threshold, over a narrow band, so the edge is a cliff that wanders like a real
    /// escarpment. Closed contours become mesas and buttes; a second, smaller tier sits on top
    /// of the first in places. Never at the shore, so coasts stay walkable.
    /// </summary>
    float Plateaus(in P4 p, double spacing, float e, float height, float cover)
    {
        double warp = Def.PlateauWavelength * 0.25;
        float w = _plateauWarp.Fbm(p, spacing);
        var q = new P4(p.X + w * warp, p.Y + w * warp * 0.7, p.Z - w * warp, p.W + w * warp * 0.4);
        // The jagged term gives the edge spurs and gullies; the edge width wanders, so some
        // stretches are sheer cliffs and others are ramps you can walk up.
        float n = _plateau.Fbm(q, spacing) + _jag.Fbm(q, spacing) * 0.11f;
        float first = 0.45f - cover * 0.9f;
        float edge = 0.045f + 0.06f * Mathx.Smooth(-0.1f, 0.6f, _region.Fbm(new P4(p.W, p.Z, p.Y, p.X), spacing));
        float lift = Mathx.Smooth(first - edge, first + edge, n) + 0.55f * Mathx.Smooth(first + 0.32f - edge, first + 0.32f + edge, n);
        return lift * height * Mathx.Smooth(2f, 18f, e);
    }

    /// <summary>
    /// A dune field: transverse dunes square to the prevailing wind, each a long gentle climb
    /// (about 18 degrees at its steepest) to a sharp brink, then a slip face falling straight at the
    /// sand's angle of rest (about 35 degrees) and easing out at its toe. The crests wander and link,
    /// rising and dipping along their length, and the dunes swell and shrink across the field. Too
    /// fine to show at coarse spacings, where they fade to their mean height.
    /// </summary>
    float Dunes(in P4 p, double x, double y, double spacing, float height)
    {
        const float Brink = 0.7f, Mean = 0.39f;
        float field = 0.55f + 0.45f * Mathx.Smooth(-0.6f, 0.6f, _duneField.Fbm(p, spacing));
        float visible = Mathx.Smooth((float)(_duneWave * 0.5), (float)(_duneWave * 0.22), (float)spacing);
        if (visible <= 0f) return height * field * Mean;
        field *= 0.78f + 0.22f * Mathx.Smooth(-0.5f, 0.5f, _duneWander.Fbm(p.Swap, spacing));
        double phase = _duneA * x / Width + _duneB * y / Height + _dune.Fbm(p, spacing) * 0.6 + _duneWander.Fbm(p, spacing) * 0.12;
        float t = (float)(phase - Math.Floor(phase));
        float shape;
        if (t < Brink)
        {
            float s = t / Brink;
            shape = s * s * (1.6f - 0.6f * s);
        }
        else
        {
            float u = (t - Brink) / (1f - Brink);
            shape = 1f - u * (1.4f - 0.4f * u * u);
        }
        return height * field * Mathx.Lerp(Mean, shape, visible);
    }

    /// <summary>Cinder cones with summit craters, on a wrapping grid, where the landform calls for them.</summary>
    float Cones(double x, double y)
    {
        float result = 0f;
        _coneGrid.Cell(x, y, out int ix, out int iy);
        for (int cj = -1; cj <= 1; cj++)
        for (int ci = -1; ci <= 1; ci++)
        {
            if (!_coneGrid.Feature(ix + ci, iy + cj, out double fx, out double fy, out ulong h)) continue;
            float presence = _conePresence[_coneGrid.Wrapped(ix + ci, iy + cj)];
            if (presence < 0.05f) continue;
            float radius = Mathx.Lerp(170f, 380f, Hash.Unit(h, 3));
            float t = (float)Distance(x, y, fx, fy) / radius;
            if (t >= 1f) continue;
            float height = Mathx.Lerp(60f, 200f, Hash.Unit(h, 4)) * Math.Min(1f, presence);
            float cone = height * MathF.Pow(1f - t, 1.5f);
            if (t < 0.24f) cone -= height * 0.3f * (1f - t * t / (0.24f * 0.24f)); // summit crater
            result = Math.Max(result, cone);
        }
        return result;
    }

    /// <summary>Where the ring structures stand (grid features the landform has raised), for views and tests.</summary>
    public System.Collections.Generic.IEnumerable<(double X, double Y, float Height)> RingStructures()
    {
        for (int k = 0; k < _ringGrid.Cells; k++)
        {
            if (_ringHeight[k] < 0.5f || !_ringGrid.Feature(k % _ringGrid.Nx, k / _ringGrid.Nx, out double fx, out double fy, out _)) continue;
            yield return (Mathx.WrapX(fx, Width), Mathx.Wrap(fy, Height), _ringHeight[k]);
        }
    }

    /// <summary>Ring structures: concentric ridges round a low dome, on a wrapping grid, where the landform calls for them.</summary>
    float Rings(double x, double y, double spacing)
    {
        float result = 0f;
        _ringGrid.Cell(x, y, out int ix, out int iy);
        float visible = Mathx.Smooth(70f, 30f, (float)spacing);
        for (int cj = -1; cj <= 1; cj++)
        for (int ci = -1; ci <= 1; ci++)
        {
            if (!_ringGrid.Feature(ix + ci, iy + cj, out double fx, out double fy, out ulong h)) continue;
            float height = _ringHeight[_ringGrid.Wrapped(ix + ci, iy + cj)];
            if (height < 0.5f) continue;
            float radius = Mathx.Lerp(480f, 900f, Hash.Unit(h, 3));
            float d = (float)Distance(x, y, fx, fy), t = d / radius;
            if (t >= 1.2f) continue;
            float wave = Mathx.Lerp(110f, 160f, Hash.Unit(h, 4));
            float ripple = Mathx.Lerp(0.5f, 0.5f - 0.5f * MathF.Cos(d / wave * MathF.Tau), visible);
            float envelope = (1f - Mathx.Smooth(0.75f, 1.2f, t)) * (0.35f + 0.65f * Mathx.Smooth(0f, 0.6f, t));
            float dome = t < 0.2f ? 0.7f * (1f - t / 0.2f) * (1f - t / 0.2f) : 0f;
            result = Math.Max(result, height * (ripple * envelope + dome));
        }
        return result;
    }

    /// <summary>
    /// A delta: from the apex the land is squeezed to the waterline and spreads out past the old
    /// shore, and distributary channels radiate seaward, splitting as they go, with pools between.
    /// </summary>
    float Delta(float e, double x, double y, float weight)
    {
        for (int d = 0; d < _deltaChannels.Length; d++)
        {
            DeltaFan f = Landforms.Deltas[d];
            double dx = Mathx.WrapDelta(x - f.ApexX, Width), dy = Mathx.WrapDelta(y - f.ApexY, Height);
            double r2 = dx * dx + dy * dy;
            if (r2 > f.Radius * f.Radius * 1.6) continue;
            float r = (float)Math.Sqrt(r2);
            float along = (float)(dx * f.AxisX + dy * f.AxisY), across = (float)(-dx * f.AxisY + dy * f.AxisX);
            // Distributaries: the nearest piece of the channel tree, by distance to its bank.
            ChannelMap map = _deltaChannels[d];
            float channel = 0f;
            int ga = (int)((along + map.Extent) / ChannelCell), gc = (int)((across + map.Extent) / ChannelCell);
            if (ga >= 0 && gc >= 0 && ga < map.Size && gc < map.Size && map.Near[gc * map.Size + ga].Length > 0)
            {
                float wobble = _deltaNoise.Sample(dx / 46.0, dy / 46.0, f.Id * 3.17 + 2.3); // no bank is a ruled line
                foreach (int k in map.Near[gc * map.Size + ga])
                {
                    Channel c = map.Channels[k];
                    float va = c.A1 - c.A0, vc = c.C1 - c.C0;
                    float t = Math.Clamp(((along - c.A0) * va + (across - c.C0) * vc) / (va * va + vc * vc), 0f, 1f);
                    float pa = along - (c.A0 + va * t), pc = across - (c.C0 + vc * t);
                    float half = c.Width * 0.5f;
                    float dist = MathF.Sqrt(pa * pa + pc * pc) + wobble * (2f + half * 0.28f);
                    if (dist < half + 12f) channel = Math.Max(channel, 1f - Mathx.Smooth(half - 3f, half + 10f, dist));
                }
            }
            float pools = Mathx.Smooth(0.3f, 0.42f, _deltaNoise.Sample(dx / 210.0, dy / 210.0, f.Id * 7.31 + 9.7) + 0.2f * _deltaNoise.Sample(dx / 63.0, dy / 63.0, f.Id * 5.13 + 4.1));
            float ground = 1.7f + 1.3f * Mathx.Smooth(f.Radius, 0f, r);
            float target = Mathx.Lerp(ground, -0.6f, pools * 0.8f);
            target = Mathx.Lerp(target, -2.4f, channel);
            // The fan builds out over the shallows; deep water stays deep.
            float reach = e > 0f ? 1f : Mathx.Smooth(-32f, -12f, e);
            return Mathx.Lerp(e, target, Math.Min(1f, weight * 1.2f) * reach);
        }
        return e;
    }

    /// <summary>Volcano cones with calderas on a jittered grid that wraps on both axes.</summary>
    float Volcanoes(double x, double y, out float heat)
    {
        float result = 0f;
        heat = 0f;
        _volcanoGrid.Cell(x, y, out int ix, out int iy);
        for (int cj = -1; cj <= 1; cj++)
        for (int ci = -1; ci <= 1; ci++)
        {
            if (!_volcanoGrid.Feature(ix + ci, iy + cj, out double fx, out double fy, out ulong h)) continue;
            float presence = Math.Min(1f, _volcanoPresence[_volcanoGrid.Wrapped(ix + ci, iy + cj)]);
            if (presence < 0.05f) continue;
            float radius = Def.VolcanoSpacing * Mathx.Lerp(0.32f, 0.55f, Hash.Unit(h, 3));
            float dx = (float)Mathx.WrapDelta(x - fx, Width), dy = (float)Mathx.WrapDelta(y - fy, Height);
            float angle = MathF.Atan2(dy, dx), phase = Hash.Unit(h, 5) * 6.2832f;
            // Lobed outline (old flows ran further on some sides) and radial gullies on the
            // flanks: together they make a cone read as a volcano rather than a dome.
            float lobes = 1f + 0.16f * MathF.Sin(angle * 3f + phase) + 0.08f * MathF.Sin(angle * 5f - phase * 1.7f);
            float t = MathF.Sqrt(dx * dx + dy * dy) / (radius * lobes);
            if (t >= 1.4f) continue;
            float height = Def.VolcanoHeight * Mathx.Lerp(0.45f, 1.15f, Hash.Unit(h, 4)) * presence;
            float cone = t < 1f ? height * MathF.Pow(1f - t, 1.7f) : 0f;
            float gully = 0.5f + 0.5f * MathF.Sin(angle * 18f + phase + t * 5f);
            cone *= 1f - 0.12f * gully * Mathx.Smooth(0.1f, 0.4f, t) * Mathx.Smooth(1f, 0.55f, t);
            if (t < 0.16f) cone -= height * 0.22f * (1f - t * t / (0.16f * 0.16f)); // caldera
            result = Math.Max(result, cone);
            heat = Math.Max(heat, Mathx.Smooth(0.75f, 0f, t) * presence);
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
    /// cells around the planet both ways, so features wrap across both seams like everything else.
    /// </summary>
    readonly struct FeatureGrid
    {
        readonly int _nx, _ny, _salt;
        readonly double _width, _height, _cw, _ch;
        readonly float _presence;
        readonly ulong _seed;

        public FeatureGrid(double width, double height, double spacing, ulong seed, int salt, float presence)
        {
            spacing = Math.Max(spacing, 1);
            _nx = Math.Max(1, (int)Math.Round(width / spacing));
            _ny = Math.Max(1, (int)Math.Round(height / spacing));
            _width = width;
            _height = height;
            _cw = width / _nx;
            _ch = height / _ny;
            _seed = seed;
            _salt = salt;
            _presence = presence;
        }

        public int Nx => _nx;
        public int Cells => _nx * _ny;

        /// <summary>The index of a cell, wrapped onto the planet.</summary>
        public int Wrapped(int ix, int iy) => ((iy % _ny + _ny) % _ny) * _nx + (ix % _nx + _nx) % _nx;

        public void Cell(double x, double y, out int ix, out int iy)
        {
            ix = (int)Math.Floor(Mathx.WrapX(x, _width) / _cw);
            iy = (int)Math.Floor(Mathx.Wrap(y, _height) / _ch);
        }

        public bool Feature(int ix, int iy, out double fx, out double fy, out ulong hash)
        {
            // The feature's position stays in the unwrapped cell (beside the query point across
            // a seam), but its hash comes from the wrapped cell, so both sides see one feature.
            fx = fy = 0;
            int wx = (ix % _nx + _nx) % _nx, wy = (iy % _ny + _ny) % _ny;
            hash = Hash.Of(_seed, wx, wy, _salt);
            if (Hash.Unit(hash, 0) > _presence) return false;
            fx = (ix + 0.15 + 0.7 * Hash.Unit(hash, 1)) * _cw;
            fy = (iy + 0.15 + 0.7 * Hash.Unit(hash, 2)) * _ch;
            return true;
        }
    }

    double Distance(double x, double y, double fx, double fy)
    {
        double dx = Mathx.WrapDelta(x - fx, Width), dy = Mathx.WrapDelta(y - fy, Height);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Jagged noise in about [-1, 1] at a few hundred metres and finer: breaks clean edges into spurs and gullies.</summary>
    public float Jag(double x, double y, double spacing) => _jag.Fbm(Torus.At(x, y), spacing);

    /// <summary>
    /// A smooth sideways shift, metres, that makes rivers wander between the corners of their
    /// coarse polylines. Applied to where a sample looks for its river, so the channel, its
    /// water and its banks all bend together. Plains and deltas meander widely, mountains hardly.
    /// </summary>
    public void Meander(double x, double y, out double dx, out double dy)
    {
        P4 p = Torus.At(x, y);
        Landforms.Sample(x, y, p, out LandformBlend blend, out _, out _);
        Meander(p, Landforms.Relief(blend).Meander, out dx, out dy);
    }

    internal void Meander(double x, double y, float scale, out double dx, out double dy) => Meander(Torus.At(x, y), scale, out dx, out dy);

    void Meander(in P4 p, float scale, out double dx, out double dy)
    {
        double amp = 26.0 * Math.Clamp(scale, 0.3f, 2.6f);
        dx = _meander.Fbm(p, 16) * amp;
        dy = _meander.Fbm(p.Swap, 16) * amp;
    }

    /// <summary>Rainfall weight in [0.4, 1.6], for river catchments.</summary>
    public float Rain(double x, double y) => 1f + 0.6f * Math.Clamp(_rain.Fbm(Torus.At(x, y), 64), -1f, 1f);

    /// <summary>Patch noise in roughly [-1, 1] for biomes that come in scattered patches (sulfur flats).</summary>
    public float Patches(double x, double y) => _patches.Fbm(Torus.At(x, y), 64);

    /// <summary>Weather noise, roughly in [-1, 1], that breaks up latitude bands and humidity rings.</summary>
    public void ClimateNoise(double x, double y, out float temperature, out float humidity)
    {
        P4 p = Torus.At(x, y);
        temperature = _rain.Fbm(p.Swap, 64);
        humidity = _special.Fbm(new P4(p.W, p.Z, p.Y, p.X), 64);
    }
}
