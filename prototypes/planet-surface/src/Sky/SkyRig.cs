using System;
using Godot;
using Planets.Look;
using PlanetSurface.World;

namespace PlanetSurface.Skies;

/// <summary>
/// Day and night, the sun and moons, and the environment: sky shader, fog, ambient light and
/// grading, all from the world's <see cref="SkySystem"/> and the current <see cref="AtmosphereState"/>.
///
/// The sun's path depends on latitude: the equator is the middle of the map and both map edges
/// are the pole (the planet is a torus), so near the edges the sun stays low all day. Moons rise
/// and set on their own periods and show phases; a neighbouring giant, if the system has one,
/// hangs almost still (the world orbits it) while its phase follows the sun.
/// </summary>
public partial class SkyRig : Node3D
{
    /// <summary>0 midnight, 0.25 sunrise-ish, 0.5 noon, 0.75 sunset-ish.</summary>
    public double TimeOfDay = 0.3;

    /// <summary>Speed-up for the day cycle (T key).</summary>
    public float TimeScale = 1f;
    public bool Paused;

    public SkySystem System { get; private set; }
    public Godot.Environment Environment { get; private set; }
    public DirectionalLight3D Sun { get; private set; }
    public DirectionalLight3D NightLight { get; private set; }
    public Vector3 ToSun { get; private set; } = Vector3.Up;
    public float Daylight { get; private set; } = 1f;
    public AtmosphereState Air { get; private set; }

    ShaderMaterial _sky;
    NoiseTexture2D _noise;
    double _days;
    Vector2 _cloudDrift;
    float _latitude;
    PlanetWorld _world;

    public override void _Ready()
    {
        _noise = new NoiseTexture2D
        {
            Width = 512, Height = 512, Seamless = true, GenerateMipmaps = true,
            Noise = new FastNoiseLite { Seed = 11, Frequency = 0.008f, FractalOctaves = 5, FractalGain = 0.5f, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin },
        };
        _sky = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky.gdshader") };
        _sky.SetShaderParameter("cloud_noise", _noise);
        _sky.SetShaderParameter("cloud_detail", new NoiseTexture3D
        {
            Width = 64, Height = 64, Depth = 64, Seamless = true,
            Noise = new FastNoiseLite { Seed = 5, Frequency = 0.06f, FractalOctaves = 3, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin },
        });
        Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky, RadianceSize = Sky.RadianceSizeEnum.Size64, ProcessMode = Sky.ProcessModeEnum.Incremental },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapWhite = 6f,
            TonemapExposure = 1.0f,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Exponential,
            FogAerialPerspective = 0.35f,
            GlowEnabled = true,
            GlowIntensity = 0.6f,
            GlowBloom = 0.03f,
            GlowHdrThreshold = 1.2f,
            AdjustmentEnabled = true,
            AdjustmentContrast = 1.06f,
        };
        AddChild(new WorldEnvironment { Environment = Environment });

        Sun = new DirectionalLight3D
        {
            Name = "Sun", ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 320f,
        };
        AddChild(Sun);
        NightLight = new DirectionalLight3D { Name = "NightLight", ShadowEnabled = false, LightEnergy = 0f, SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly };
        AddChild(NightLight);
        Air = Atmosphere.Compute(SkySystem.For(new Planets.Gen.PlanetSpec(1, Planets.Gen.Archetype.Temperate, 0, 0)), Atmosphere.Clear(), 0.5f);
    }

    public void SetWorld(PlanetWorld world)
    {
        _world = world;
        System = world.Sky;
        SkySystem s = System;
        _sky.SetShaderParameter("zenith_day", C(s.Zenith));
        _sky.SetShaderParameter("horizon_day", C(s.Horizon));
        _sky.SetShaderParameter("zenith_night", C(s.NightZenith));
        _sky.SetShaderParameter("sunset", C(s.Sunset));
        _sky.SetShaderParameter("haze", C(s.Haze));
        _sky.SetShaderParameter("star_color", C(s.StarColor));
        _sky.SetShaderParameter("sun_radius", s.SunRadius);
        _sky.SetShaderParameter("sun_energy", s.SunEnergy);
        _sky.SetShaderParameter("density", s.Density);
        _sky.SetShaderParameter("stars_by_day", s.StarsByDay);
        _sky.SetShaderParameter("nebula", s.Nebula);
        _sky.SetShaderParameter("nebula_a", C(s.NebulaA));
        _sky.SetShaderParameter("nebula_b", C(s.NebulaB));
        // The noise tile must divide the planet both ways, so clouds join across the seams.
        float W = (float)world.Width, H = (float)world.Height;
        _sky.SetShaderParameter("cloud_scale", new Vector2(W / MathF.Max(1, MathF.Round(W / 8000f)), H / MathF.Max(1, MathF.Round(H / 8000f))));
        int moons = 0;
        var moonColors = new Vector3[3];
        var moonRadii = new float[3];
        bool giant = false;
        foreach (SkyBody b in s.Bodies)
        {
            if (b.Kind == BodyKind.GasGiant)
            {
                giant = true;
                _sky.SetShaderParameter("giant_radius", b.Radius);
                _sky.SetShaderParameter("giant_color", C(b.Color));
                _sky.SetShaderParameter("giant_band", C(b.Band));
                _sky.SetShaderParameter("rings", b.Rings);
                _sky.SetShaderParameter("ring_color", C(b.RingColor));
            }
            else if (moons < 3)
            {
                moonColors[moons] = new Vector3(b.Color.R, b.Color.G, b.Color.B);
                moonRadii[moons] = b.Radius;
                moons++;
            }
        }
        _sky.SetShaderParameter("giant", giant);
        _sky.SetShaderParameter("moon_count", moons);
        _sky.SetShaderParameter("moon_color", moonColors);
        _sky.SetShaderParameter("moon_radius", moonRadii);
        Sun.LightColor = C(s.StarColor);
    }

    /// <summary>Sets the time and the weather's air for this frame. <paramref name="latitude"/> is -1 (one pole) to 1 (the other).</summary>
    public void Advance(double delta, float latitude, AtmosphereState air, Vector2 wind)
    {
        if (System == null) return;
        _latitude = latitude;
        Air = air;
        if (!Paused) TimeOfDay = (TimeOfDay + delta * TimeScale / (System.DayMinutes * 60.0)) % 1.0;
        _days += Paused ? 0 : delta * TimeScale / (System.DayMinutes * 60.0);
        _cloudDrift += wind * (float)delta * 1.8f;
        Apply();
    }

    /// <summary>Recomputes lights and the sky from the current time and air (screenshots call it directly).</summary>
    public void Apply()
    {
        SkySystem s = System;
        float hour = (float)(TimeOfDay * Math.Tau - Math.PI);   // 0 at noon
        float lat = _latitude * 1.3f;                            // up to about 75 degrees at the poles
        Vector3 toSun = Direction(hour, lat, 0.18f, 0f);
        ToSun = toSun;
        float day = Smooth(-0.12f, 0.12f, toSun.Y);
        Daylight = day;
        AtmosphereState air = Air;

        // The sun: star-coloured, warming toward the horizon, dimmed by weather.
        float low = Smooth(0.35f, 0f, toSun.Y);
        Rgb light = Rgb.Lerp(s.StarColor, s.Sunset, low * 0.7f);
        Sun.LightColor = C(light);
        Sun.LightEnergy = s.SunEnergy * Smooth(-0.05f, 0.1f, toSun.Y) * air.SunScale;
        Sun.ShadowOpacity = Math.Clamp(0.25f + air.SunScale, 0f, 1f);
        // The sun stays the sky's light even below the horizon (the shader reads its direction for
        // twilight); it just gives no light and casts no shadows then.
        Sun.ShadowEnabled = toSun.Y > -0.03f;
        Sun.LookAt(Sun.GlobalPosition - toSun, MathF.Abs(toSun.Y) > 0.99f ? Vector3.Forward : Vector3.Up);

        // Moons: their own hour angles; the giant nearly fixed in the sky.
        int m = 0;
        var moonDirs = new Vector3[3];
        Vector3 brightest = Vector3.Up;
        float brightestEnergy = 0.06f; // starlight
        foreach (SkyBody b in s.Bodies)
        {
            if (b.Kind == BodyKind.GasGiant)
            {
                float sway = (float)Math.Sin(_days * Math.Tau / b.Period) * 0.03f;
                Vector3 dir = AzimuthElevation(b.Azimuth + sway, b.Inclination);
                _sky.SetShaderParameter("giant_dir", dir);
                _sky.SetShaderParameter("ring_normal", new Vector3(MathF.Sin(b.RingTilt) * 0.6f, MathF.Cos(b.RingTilt), MathF.Sin(b.RingTilt) * 0.8f).Normalized());
                float lit = Math.Clamp(-dir.Dot(-toSun) * 0.5f + 0.5f, 0f, 1f);
                if (dir.Y > 0f && 0.25f * lit > brightestEnergy) { brightestEnergy = 0.25f * lit; brightest = dir; }
            }
            else if (m < 3)
            {
                float angle = (float)((_days / b.Period + b.Phase0) * Math.Tau);
                Vector3 dir = Direction(angle - (float)Math.PI, lat, b.Inclination - 0.5f, b.Azimuth);
                moonDirs[m++] = dir;
                float phase = Math.Clamp(dir.Dot(toSun) * -0.5f + 0.5f, 0f, 1f); // full when opposite the sun
                float e = 0.22f * phase * Smooth(-0.02f, 0.2f, dir.Y) * (b.Radius / 0.03f);
                if (e > brightestEnergy) { brightestEnergy = e; brightest = dir; }
            }
        }
        _sky.SetShaderParameter("moon_dir", moonDirs);

        // Night light: the brightest moon (or the giant's reflected light, or starlight), so night is dark but playable.
        NightLight.LightEnergy = (1f - day) * Math.Clamp(brightestEnergy, 0.06f, 0.35f) * air.SunScale;
        NightLight.LightColor = new Color(0.62f, 0.7f, 0.95f);
        NightLight.LookAt(NightLight.GlobalPosition - (brightest.Y > 0.1f ? brightest : new Vector3(0.3f, 1f, 0.2f).Normalized()), Vector3.Forward);

        // The stars turn with the day.
        _sky.SetShaderParameter("star_turn", new Basis(new Vector3(1, 0, 0).Rotated(Vector3.Up, 0.3f), (float)(TimeOfDay * Math.Tau)));

        // Clouds and overcast from the weather.
        _sky.SetShaderParameter("cloud_cover", air.CloudCover);
        _sky.SetShaderParameter("cloud_dark", air.CloudDarkness);
        _sky.SetShaderParameter("cloud_base", air.CloudBase);
        _sky.SetShaderParameter("cloud_thick", air.CloudThickness);
        _sky.SetShaderParameter("cloud_drift", _cloudDrift);
        _sky.SetShaderParameter("overcast", air.Overcast);
        Rgb grey = Rgb.Lerp(Rgb.Lerp(s.Horizon, new Rgb(0.62f, 0.64f, 0.66f), 0.7f), new Rgb(0.2f, 0.21f, 0.24f), air.CloudDarkness);
        _sky.SetShaderParameter("overcast_color", C(grey));
        _sky.SetShaderParameter("cloud_lit", C(Rgb.Lerp(new Rgb(1f, 1f, 1f), s.StarColor, 0.12f) * s.CloudTint));
        _sky.SetShaderParameter("cloud_shade", C(Rgb.Lerp(Rgb.Lerp(s.Zenith, new Rgb(0.55f, 0.58f, 0.62f), 0.6f) * s.CloudTint, new Rgb(0.16f, 0.17f, 0.2f), air.CloudDarkness)));

        // Fog: the air's haze colour, lit by the sun, greyed by overcast, dark at night.
        Rgb fog = Rgb.Lerp(s.Haze, grey, air.Overcast * 0.8f) * (0.08f + 0.92f * day);
        fog = Rgb.Lerp(fog, s.Sunset * 0.8f, low * day * 0.35f);
        Environment.FogLightColor = C(fog);
        Environment.FogDensity = air.FogDensity;
        Environment.FogSkyAffect = air.FogSkyAffect;
        Environment.FogSunScatter = 0.15f + 0.25f * s.Density * (1f - air.Overcast);
        // Ash worlds are lit as much by their lava as their sun: never quite black.
        float floor = s.Air == AirKind.Ash ? 0.45f : 0.25f;
        Environment.AmbientLightEnergy = (floor + (1f - floor) * day) * air.AmbientScale;
        Environment.AdjustmentSaturation = air.Saturation;
    }

    /// <summary>
    /// Pools valley fog below a height: the low ground around the player plus the fog's depth. Godot's
    /// height fog thickens below that plane, so valleys fill and ridges stand clear.
    /// </summary>
    public void SetValleyFog(float floor, AtmosphereState air)
    {
        Environment.FogHeight = floor + air.ValleyFogDepth;
        Environment.FogHeightDensity = air.ValleyFog * 0.09f;
    }

    /// <summary>A direction in the sky from an hour angle (0 = highest), latitude, declination and a turn of the whole path.</summary>
    static Vector3 Direction(float hour, float latitude, float declination, float turn)
    {
        // Equatorial coordinates to horizon coordinates: x east, y up, z south.
        float sinAlt = MathF.Sin(declination) * MathF.Sin(latitude) + MathF.Cos(declination) * MathF.Cos(latitude) * MathF.Cos(hour);
        float alt = MathF.Asin(Math.Clamp(sinAlt, -1f, 1f));
        float az = MathF.Atan2(-MathF.Sin(hour) * MathF.Cos(declination),
            MathF.Sin(declination) * MathF.Cos(latitude) - MathF.Cos(declination) * MathF.Sin(latitude) * MathF.Cos(hour));
        return AzimuthElevation(az + turn, alt);
    }

    /// <summary>Azimuth (0 north, clockwise) and elevation to a direction: -Z is north.</summary>
    static Vector3 AzimuthElevation(float azimuth, float elevation) =>
        new(MathF.Sin(azimuth) * MathF.Cos(elevation), MathF.Sin(elevation), -MathF.Cos(azimuth) * MathF.Cos(elevation));

    static Color C(Rgb c) => new(c.R, c.G, c.B);

    static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
