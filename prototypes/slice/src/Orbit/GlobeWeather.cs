using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using PlanetSurface.Skies;
using Slice.Surface;
using Weather.Model;

namespace Slice.Orbit;

/// <summary>
/// The live weather on the globe: a low-resolution layer over the whole map, read from the weather
/// the surface runs (<see cref="WeatherSim"/>), that the globe's cloud sheet lays over the sky's own
/// cloud noise. Each texel is what the sky over that place shows: its region's weather, the front's
/// deck trailing behind its edge and the open air ahead of it, the storm cells' towers and anvils,
/// and the low haze of dust, blowing snow and fog. The sheet works its clouds out of it with the
/// sky's own sums (sky.gdshader's cloud_density, laid flat), so the clouds over a place from orbit are
/// the ones over it from the ground.
///
/// It is filled a few texels a frame on the main thread, inside a small time budget, and uploaded
/// whole when a pass is done; the sheet blends from the last pass to the newest over the time the
/// next one takes, so the weather moves smoothly with nothing to hitch on.
/// </summary>
public sealed class GlobeWeather
{
    /// <summary>About how much of the map one texel covers, m.</summary>
    public const float TexelMetres = 125f;

    /// <summary>The most a frame spends filling texels, ms, and the shortest time between passes, s.</summary>
    public const double BudgetMs = 0.5, LeastPeriod = 3.0;

    public int Width { get; }
    public int Height { get; }
    public Vector2 MapSize { get; }

    /// <summary>Passes finished so far, and how long the last one took, s (game time).</summary>
    public int Passes { get; private set; }
    public double Period { get; private set; } = LeastPeriod;

    /// <summary>The longest any one frame spent on it, ms (tests read this).</summary>
    public double WorstFrameMs { get; set; }

    readonly byte[] _cloud, _haze;
    readonly Image[] _cloudImage = new Image[2], _hazeImage = new Image[2];
    readonly ImageTexture[] _cloudTexture = new ImageTexture[2], _hazeTexture = new ImageTexture[2];
    int _newest, _next;
    double _clock, _passStarted, _lastDone, _every = LeastPeriod;
    ShaderMaterial _sheet;
    double _lastStep = -1;
    readonly Vector4[] _strikes = new Vector4[4];
    int _strikeAt;

    /// <summary>The cloud layer's last finished pass, for tests: cover, extra coverage (signed, as stored), tower, veil.</summary>
    public byte[] Finished { get; }

    /// <summary>The haze layer's last finished pass: dust, snow, fog, lightning.</summary>
    public byte[] FinishedHaze { get; }

    public GlobeWeather(Vector2 mapSize)
    {
        MapSize = mapSize;
        Width = Math.Clamp((int)MathF.Round(mapSize.X / TexelMetres), 32, 256);
        Height = Math.Max(16, (int)MathF.Round(mapSize.Y / (mapSize.X / Width)));
        _cloud = new byte[Width * Height * 4];
        _haze = new byte[Width * Height * 4];
        Finished = new byte[_cloud.Length];
        FinishedHaze = new byte[_haze.Length];
        // Until the first pass is in: the sky's look of a quiet day, no extra cloud anywhere.
        for (int i = 0; i < _cloud.Length; i += 4)
        {
            _cloud[i] = 64;
            _cloud[i + 1] = 128;
            _cloud[i + 3] = 128;
        }
        for (int k = 0; k < 2; k++)
        {
            _cloudImage[k] = Image.CreateFromData(Width, Height, false, Image.Format.Rgba8, _cloud);
            _hazeImage[k] = Image.CreateFromData(Width, Height, false, Image.Format.Rgba8, _haze);
            _cloudTexture[k] = ImageTexture.CreateFromImage(_cloudImage[k]);
            _hazeTexture[k] = ImageTexture.CreateFromImage(_hazeImage[k]);
        }
        Array.Copy(_cloud, Finished, _cloud.Length);
    }

    /// <summary>The map point a texel stands for, m.</summary>
    public Vector3 PointOf(int i, int j) => new((i + 0.5f) / Width * MapSize.X, 0f, (j + 0.5f) / Height * MapSize.Y);

    /// <summary>The texel over a map point.</summary>
    public (int i, int j) TexelOf(float x, float z)
    {
        int i = (int)MathF.Floor(Wrap(x, MapSize.X) / MapSize.X * Width), j = (int)MathF.Floor(Wrap(z, MapSize.Y) / MapSize.Y * Height);
        return (Math.Clamp(i, 0, Width - 1), Math.Clamp(j, 0, Height - 1));
    }

    /// <summary>The sheet's cloud material reads the layer from here on.</summary>
    public void Bind(ShaderMaterial sheet, Color cloudColor)
    {
        _sheet = sheet;
        sheet.SetShaderParameter("cloud_color", cloudColor);
        Swap();
        sheet.SetShaderParameter("weather_blend", 1f);
        sheet.SetShaderParameter("live", 1f);
    }

    void Swap()
    {
        _sheet?.SetShaderParameter("weather_was", _cloudTexture[1 - _newest]);
        _sheet?.SetShaderParameter("weather_now", _cloudTexture[_newest]);
        _sheet?.SetShaderParameter("haze_was", _hazeTexture[1 - _newest]);
        _sheet?.SetShaderParameter("haze_now", _hazeTexture[_newest]);
    }

    /// <summary>
    /// One drawn frame: a few more texels of the pass under way, the sheet's blend and clock, the sky's
    /// own noise and drift, and any strike that has just come down. <paramref name="sim"/> null: no weather.
    /// A new pass starts no sooner than <paramref name="every"/> seconds after the last began.
    /// </summary>
    public void Update(float dt, WeatherSim sim, SkyRig sky, Color dust, double every = LeastPeriod)
    {
        _every = Math.Max(every, LeastPeriod);
        long started = Stopwatch.GetTimestamp();
        _clock += dt;
        if (_sheet != null)
        {
            sky.ShareCloudBase(_sheet);
            _sheet.SetShaderParameter("dust_color", dust);
            _sheet.SetShaderParameter("clock", (float)_clock);
        }
        if (sim != null)
        {
            Strikes(sim);
            Fill(sim, sky.System, BudgetMs);
        }
        else if (Passes == 0) Passes = 1; // nothing to read: the quiet layer it started with stands
        if (_sheet != null)
        {
            float blend = (float)Math.Clamp((_clock - _lastDone) / Math.Max(Period, 0.1), 0.0, 1.0);
            _sheet.SetShaderParameter("weather_blend", Passes < 2 ? 1f : blend);
        }
        WorstFrameMs = Math.Max(WorstFrameMs, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    /// <summary>Fills texels of the pass under way for up to <paramref name="budgetMs"/>; true if it finished a pass.</summary>
    public bool Fill(WeatherSim sim, SkySystem sky, double budgetMs)
    {
        if (_next == 0)
        {
            if (Passes > 0 && _clock - _passStarted < _every) return false;
            _passStarted = _clock;
        }
        long started = Stopwatch.GetTimestamp();
        var cells = Cells(sim);
        int total = Width * Height;
        while (_next < total)
        {
            int i = _next % Width, j = _next / Width;
            Write(_next, At(sim, sky, PointOf(i, j), MapSize, cells));
            _next++;
            if ((_next & 7) == 0 && Stopwatch.GetElapsedTime(started).TotalMilliseconds > budgetMs) return false;
        }
        _next = 0;
        Finish();
        return true;
    }

    /// <summary>A whole pass at once (tests, and a first look before anyone flies).</summary>
    public void FillAll(WeatherSim sim, SkySystem sky)
    {
        _next = 0;
        _passStarted = _clock - _every;
        while (!Fill(sim, sky, double.MaxValue)) { }
    }

    void Finish()
    {
        _newest = 1 - _newest;
        _cloudImage[_newest].SetData(Width, Height, false, Image.Format.Rgba8, _cloud);
        _hazeImage[_newest].SetData(Width, Height, false, Image.Format.Rgba8, _haze);
        _cloudTexture[_newest].Update(_cloudImage[_newest]);
        _hazeTexture[_newest].Update(_hazeImage[_newest]);
        Array.Copy(_cloud, Finished, _cloud.Length);
        Array.Copy(_haze, FinishedHaze, _haze.Length);
        Period = Passes == 0 ? LeastPeriod : Math.Max(_clock - _lastDone, 0.1);
        _lastDone = _clock;
        Passes++;
        Swap();
    }

    void Write(int k, in Texel t)
    {
        int o = k * 4;
        _cloud[o] = Byte(t.Cover);
        _cloud[o + 1] = Byte(t.Extra * 0.5f + 0.5f);
        _cloud[o + 2] = Byte(t.Tower);
        _cloud[o + 3] = Byte(t.Veil * 0.5f + 0.5f);
        _haze[o] = Byte(t.Dust);
        _haze[o + 1] = Byte(t.Snow);
        _haze[o + 2] = Byte(t.Fog);
        _haze[o + 3] = Byte(t.Lightning / LightningMost);
    }

    /// <summary>The flashes a minute that fill the haze layer's lightning channel.</summary>
    public const float LightningMost = 12f;

    static byte Byte(float x) => (byte)Math.Clamp((int)MathF.Round(x * 255f), 0, 255);

    /// <summary>A finished texel back as numbers (tests): what the sheet reads there.</summary>
    public Texel Read(int i, int j)
    {
        int o = (j * Width + i) * 4;
        return new Texel(Finished[o] / 255f, Finished[o + 1] / 255f * 2f - 1f, Finished[o + 2] / 255f, Finished[o + 3] / 255f * 2f - 1f,
            FinishedHaze[o] / 255f, FinishedHaze[o + 1] / 255f, FinishedHaze[o + 2] / 255f, FinishedHaze[o + 3] / 255f * LightningMost);
    }

    /// <summary>The last finished pass as a picture (looking into it): red the cover, green the extra coverage, blue the towers.</summary>
    public Image Picture()
    {
        var bytes = new byte[Width * Height * 3];
        for (int k = 0; k < Width * Height; k++)
        {
            bytes[k * 3] = Finished[k * 4];
            bytes[k * 3 + 1] = Finished[k * 4 + 1];
            bytes[k * 3 + 2] = Finished[k * 4 + 2];
        }
        return Image.CreateFromData(Width, Height, false, Image.Format.Rgb8, bytes);
    }

    /// <summary>The strikes the weather has just let down, for the sheet to flash where they fell.</summary>
    void Strikes(WeatherSim sim)
    {
        if (sim.Time == _lastStep) return;
        _lastStep = sim.Time;
        foreach (WeatherEvent e in sim.Events)
        {
            if (e.Kind != WeatherEventKind.LightningStrike) continue;
            _strikes[_strikeAt] = new Vector4(Wrap(e.Position.X, MapSize.X), Wrap(e.Position.Z, MapSize.Y), (float)_clock, 1f);
            _strikeAt = (_strikeAt + 1) % _strikes.Length;
        }
        _sheet?.SetShaderParameter("strikes", _strikes);
    }

    // ---- What the sky over a place shows -------------------------------------------------------

    /// <summary>
    /// One texel: the sky's cloud cover there (its <c>cloud_cover</c>), the coverage the front and the
    /// cells add or take away (cloud_density's sums without height), how tall the storm stands
    /// (0 to 1: a cell's tower and anvil, the line of storms along a front's edge), what lies over or
    /// thins the deck (+: the thin sheet its anvils spread ahead of a front, −: its deck trailing
    /// far behind the edge, lower and thinner), and the low haze: dust, blowing snow and ground fog
    /// (0 to 1), and the lightning, flashes a minute.
    /// </summary>
    public readonly record struct Texel(float Cover, float Extra, float Tower, float Veil, float Dust, float Snow, float Fog, float Lightning);

    /// <summary>A storm cell as the sky draws it: where (map), its rain's radius, how strong.</summary>
    public readonly record struct Cell(Vector2 At, float Radius, float Strength);

    readonly List<Cell> _cells = new();

    List<Cell> Cells(WeatherSim sim)
    {
        _cells.Clear();
        Conditions c = sim.Current;
        foreach (StormCell cell in sim.Cells)
        {
            // As SurfaceWeather.SyncCells hands them to the sky.
            if (_cells.Count >= 4) break;
            float strength = cell.Strength * c.Cellular;
            if (strength < 0.04f) continue;
            _cells.Add(new Cell(new Vector2(Wrap(cell.Position.X, MapSize.X), Wrap(cell.Position.Z, MapSize.Y)), cell.Radius, strength));
        }
        return _cells;
    }

    /// <summary>What the sky over a map point shows (see <see cref="Texel"/>).</summary>
    public static Texel At(WeatherSim sim, SkySystem sky, Vector3 p, Vector2 mapSize, IReadOnlyList<Cell> cells)
    {
        Conditions air = SurfaceWeather.AirAt(sim, p);
        AtmosphereState a = Atmosphere.Compute(sky, air, 0.5f);
        Vector2 wind = sim.Current.Wind;
        wind = wind.LengthSquared() > 0.01f ? wind.Normalized() : new Vector2(1f, 0f);

        // The cells (storm_at): a tower over the cell and its anvil spread downwind up top, seen from over it.
        float storm = 0f, halo = 0f, tower = 0f;
        var here = new Vector2(p.X, p.Z);
        foreach (Cell c in cells)
        {
            float w = c.Strength, reach = c.Radius * (1.2f + 3f * w);
            float d = Wrapped(here - c.At, mapSize).Length();
            float anvil = Wrapped(here - (c.At + wind * reach * 1.1f), mapSize).Length();
            float core = w * Smooth(reach, reach * 0.2f, d), spread = w * Smooth(reach * 1.9f, reach * 0.4f, anvil);
            storm = MathF.Max(storm, MathF.Max(core, spread));
            halo = MathF.Max(halo, w * Smooth(reach * 1.9f, reach * 0.4f, d));
            // The tower stands up over the cell; its anvil is the flat sheet spread from its top, cloud but no tower.
            tower = MathF.Max(tower, core * (0.45f + 0.55f * w * w));
        }

        // The front (front_sides): its deck behind the edge, open air ahead of it, its anvils' sheet up high.
        float extra = 0f, veil = 0f;
        if (sim.Front is { } f && f.Strength > 0.001f)
        {
            float ahead = f.Ahead(p), st = f.Strength;
            float deck = st * Smooth(150f, -1200f, ahead) * Smooth(-f.Depth, -f.Depth * 0.6f, ahead);
            float open = st * (1f - Smooth(700f, -100f, ahead) * Smooth(-f.Depth * 1.05f, -f.Depth * 0.75f, ahead));
            halo *= 1f - open;
            float thin = 0.6f * open;
            extra += deck * 0.75f - open * 0.55f - storm * 0.55f * thin;
            storm *= 1f - thin;
            tower *= 1f - thin;
            if (f.Kind == FrontKind.Storm)
            {
                veil = st * Smooth(-1500f, 0f, ahead) * Smooth(MathF.Min(9000f + 5000f * f.Severity, f.Reach - 300f), 1000f, ahead);
                // Its cells line up just behind the edge, all along it (the weather stands up only the ones
                // round you; its shelf runs the whole way): from over it a line of storms.
                float line = st * Smooth(250f, -300f, ahead) * Smooth(-1800f, -900f, ahead) * (0.5f + 0.5f * f.Severity);
                tower = MathF.Max(tower, line * 0.6f * (1f - thin));
            }
            // Far behind the edge the deck it trails is lower and thinner: grey sheet, not towers.
            veil -= deck * Smooth(-1300f, -2600f, ahead);
        }
        extra += storm * 0.55f + halo * 0.4f;
        return new Texel(a.CloudCover, Math.Clamp(extra, -1f, 1f), Math.Clamp(tower, 0f, 1f), Math.Clamp(veil, -1f, 1f),
            Math.Clamp(air.Dust, 0f, 1f), Math.Clamp(air.Snow, 0f, 1f), a.GroundFog, MathF.Max(air.LightningPerMinute, 0f));
    }

    /// <summary>The sky's coverage at a point from its noise value and a texel (cloud_density without height): over 0 there is cloud.</summary>
    public static float Coverage(float noise, in Texel t) => noise - (1f - (0.12f + (1.05f - 0.12f) * t.Cover)) + t.Extra;

    static Vector2 Wrapped(Vector2 d, Vector2 size) => new(d.X - size.X * MathF.Round(d.X / size.X), d.Y - size.Y * MathF.Round(d.Y / size.Y));

    static float Wrap(float x, float size) => x - size * MathF.Floor(x / size);

    static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
