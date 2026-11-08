using System;
using System.Collections.Generic;
using GalHack.Stars;
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

    /// <summary>The system this sky looks out at.</summary>
    public StarSystem Model => _world?.StarSystem;

    /// <summary>The one clock: days since the system's day 0, its fraction the time of day. A save holds this.</summary>
    public double Days
    {
        get => _wholeDays + TimeOfDay;
        set
        {
            _wholeDays = Math.Floor(value);
            TimeOfDay = value - _wholeDays;
        }
    }

    /// <summary>Takes this sky's directions (the world's, which at a point on the ground are east, up, south) to the star field's frame.</summary>
    public Basis SkyToStars { get; private set; } = Basis.Identity;

    /// <summary>Toward the star in the star field's frame.</summary>
    public Vector3 StarInField { get; private set; } = Vector3.Up;

    /// <summary>The radius of the sphere this world's own ring stands round, m: 0 for the true globe. Over the cap that is the curved sheet you can see.</summary>
    public float RingRadius;

    /// <summary>What every other body looks like from afar.</summary>
    public BodySurfaces Surfaces { get; private set; }

    /// <summary>The ring plane's normal of the giant this world circles, in this sky.</summary>
    public Vector3 NeighbourRing { get; private set; } = Vector3.Up;

    /// <summary>What this sky is drawing right now, for checking against the model: each body and its direction in this sky.</summary>
    public IReadOnlyList<(Orbiter Body, Vector3 Direction, float Angular)> Drawn => _drawn;
    public Godot.Environment Environment { get; private set; }
    public DirectionalLight3D Sun { get; private set; }
    public DirectionalLight3D NightLight { get; private set; }

    /// <summary>
    /// A small turn of the light that casts the shadows off the sun's true way (a game sets it, for
    /// something wrong nearby): the sun stays where it truly is in the sky, drawn by a light of its
    /// own that lights nothing, and only the shadows fall a little wrong. Identity is no turn.
    /// </summary>
    public Basis ShadowSkew = Basis.Identity;
    DirectionalLight3D _skySun;

    void Skew()
    {
        if (ShadowSkew.IsEqualApprox(Basis.Identity))
        {
            if (_skySun is { Visible: true })
            {
                _skySun.Visible = false;
                Sun.SkyMode = DirectionalLight3D.SkyModeEnum.LightAndSky;
            }
            return;
        }
        if (_skySun == null)
        {
            _skySun = new DirectionalLight3D { Name = "SkySun", SkyMode = DirectionalLight3D.SkyModeEnum.SkyOnly, ShadowEnabled = false };
            AddChild(_skySun);
        }
        _skySun.Visible = true;
        _skySun.GlobalBasis = Sun.GlobalBasis;
        _skySun.LightEnergy = Sun.LightEnergy;
        _skySun.LightColor = Sun.LightColor;
        Sun.SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly;
        Sun.GlobalBasis = ShadowSkew * Sun.GlobalBasis;
    }
    public Vector3 ToSun { get; private set; } = Vector3.Up;
    public float Daylight { get; private set; } = 1f;
    public AtmosphereState Air { get; private set; }

    ShaderMaterial _sky;
    NoiseTexture2D _noise;
    double _wholeDays;
    Basis _skyToGlobe = Basis.Identity;
    D3 _eye = new(0, 1, 0);
    bool _stood;
    const int BodySlots = 10;
    readonly List<Sighting> _seen = new(), _inSlots = new();
    readonly List<(Orbiter Body, Vector3 Direction, float Angular)> _drawn = new();
    readonly Vector3[] _bodyDirs = new Vector3[BodySlots], _bodyLights = new Vector3[BodySlots], _bodyColors = new Vector3[BodySlots];
    readonly float[] _bodyRadii = new float[BodySlots];
    readonly int[] _bodyLayers = new int[BodySlots];
    readonly Vector3[] _bodyAxes = new Vector3[BodySlots], _bodyZeros = new Vector3[BodySlots];
    Vector3 _brightest = Vector3.Up;
    float _brightestEnergy = 0.06f;
    Vector2 _cloudDrift = new(1370f, 620f); // where the cloud pattern starts: its first cloud edges don't fall on the sky checks' sight lines
    PlanetWorld _world;

    /// <summary>The galaxy and the sky from where you are: the stars and nebulae the sky draws, and the star map reads.</summary>
    public GalaxySky Stars { get; private set; }

    /// <summary>The cloud layer from above: the sky draws clouds behind the ground, so from over them this mesh draws what is under you.</summary>
    public MeshInstance3D CloudDeck { get; private set; }
    ShaderMaterial _deck;

    /// <summary>How much of the cloud deck shows, on top of what the eye's height over it allows, 0 to 1 (the air thinning away).</summary>
    public float CloudDeckFade = 1f;

    /// <summary>How the cloud layer curves, 1 / m: nothing from the ground and at flight heights, then the ground's own bend as the climb brings the globe in.</summary>
    public float CloudBend;

    /// <summary>
    /// Keeps the cloud deck under an eye: it sits in the layer, shows once the eye is climbing
    /// through it, and is lit as the sky's clouds are.
    /// </summary>
    public void PlaceCloudDeck(Vector3 eye)
    {
        AtmosphereState air = Air;
        float seen = Smooth(air.CloudBase, air.CloudBase + air.CloudThickness * 0.7f, eye.Y) * CloudDeckFade;
        CloudDeck.Visible = seen > 0.002f && air.CloudCover > 0.02f;
        InCloud(eye, air, seen);
        if (!CloudDeck.Visible) return;
        CloudDeck.GlobalPosition = new Vector3(eye.X, air.CloudBase + air.CloudThickness * 0.45f, eye.Z);
        _deck.SetShaderParameter("seen", seen);
        _deck.SetShaderParameter("bend", CloudBend);
        _deck.SetShaderParameter("cover", air.CloudCover);
        _deck.SetShaderParameter("dark", air.CloudDarkness);
        _deck.SetShaderParameter("drift", _cloudDrift);
        _deck.SetShaderParameter("scale", CloudScale);
        _deck.SetShaderParameter("noise", _noise);
        _deck.SetShaderParameter("light", Daylight * (0.35f + 0.65f * Smooth(0f, 0.3f, ToSun.Y)));
    }

    /// <summary>How thick the cloud is around the eye, 0 to 1.</summary>
    public float EyeInCloud { get; private set; }

    /// <summary>
    /// The sky draws clouds behind everything, so a vessel or a peak in cloud needs the air itself
    /// to thicken: one sample of the sky's own cloud at the eye turns the fog to a whiteout, and a
    /// fog gathering upward from the cloud base softens peaks that stand up into the layer.
    /// </summary>
    void InCloud(Vector3 eye, AtmosphereState air, float seen)
    {
        Environment.FogHeightDensity = air.ValleyFog < 0.05f ? -0.0045f * air.CloudCover * (1f - seen) : Environment.FogHeightDensity;
        if (air.ValleyFog < 0.05f) Environment.FogHeight = air.CloudBase + air.CloudThickness * 0.2f;
        Clouds ??= CloudCoverage.Of(_noise);
        float d = Clouds?.Density(eye.X, eye.Y, eye.Z, air.CloudCover, air.CloudBase, air.CloudThickness, _cloudDrift, CloudScale) ?? 0f;
        EyeInCloud = Mathf.Lerp(EyeInCloud, d, 0.2f);
        if (EyeInCloud < 0.01f) return;
        Environment.FogDensity = MathF.Max(Environment.FogDensity, 0.03f * EyeInCloud);
        Environment.FogLightColor = Environment.FogLightColor.Lerp(new Color(0.8f, 0.82f, 0.86f) * (0.25f + 0.75f * Daylight), EyeInCloud);
        Environment.FogSkyAffect = MathF.Max(Environment.FogSkyAffect, EyeInCloud);
    }

    /// <summary>The sky's cloud noise on the CPU, once it has been generated.</summary>
    public CloudCoverage Clouds { get; private set; }

    /// <summary>The world metres a tile of the cloud noise covers.</summary>
    public Vector2 CloudScale => (Vector2)_sky.GetShaderParameter("cloud_scale");

    /// <summary>The clouds' drift so far, metres.</summary>
    public Vector2 CloudDrift => _cloudDrift;

    public override void _Ready()
    {
        _deck = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/clouddeck.gdshader") };
        CloudDeck = new MeshInstance3D
        {
            Name = "CloudDeck", Mesh = new PlaneMesh { Size = new Vector2(24000f, 24000f), SubdivideWidth = 31, SubdivideDepth = 31 }, MaterialOverride = _deck, Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
        };
        AddChild(CloudDeck);
        _noise = new NoiseTexture2D
        {
            Width = 512, Height = 512, Seamless = true, GenerateMipmaps = true,
            Noise = new FastNoiseLite { Seed = 11, Frequency = 0.008f, FractalOctaves = 5, FractalGain = 0.5f, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin },
        };
        GalaxyShader.Register();
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
            // Glow only from what is really bright (the sun's disc, lightning, lava), so a low sun's
            // bright sky and lit haze never bloom over the whole picture.
            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowBloom = 0.02f,
            GlowHdrThreshold = 2.0f,
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
        NightLight = new DirectionalLight3D
        {
            Name = "NightLight", ShadowEnabled = false, LightEnergy = 0f, SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits, DirectionalShadowMaxDistance = 320f,
        };
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
        _sky.SetShaderParameter("sun_radius", world.StarSystem.See(world.StarSystem.Star, 0.0, new D3(0, world.StarSystem.Home.Radius, 0)).Angular);
        _sky.SetShaderParameter("sun_energy", s.SunEnergy);
        _sky.SetShaderParameter("density", s.Density);
        _sky.SetShaderParameter("stars_by_day", s.StarsByDay);
        // The game's seed is the galaxy's; the world found its home star in it when it was built.
        world.ChartStars();
        Stars = new GalaxySky(world.Catalogue, world.HomeStar, world.Spec.Seed);
        Stars.Bind(_sky);
        // The noise tile must divide the planet both ways, so clouds join across the seams.
        float W = (float)world.Width, H = (float)world.Height;
        _sky.SetShaderParameter("cloud_scale", new Vector2(W / MathF.Max(1, MathF.Round(W / 8000f)), H / MathF.Max(1, MathF.Round(H / 8000f))));
        Orbiter giant = world.StarSystem.Neighbour;
        _sky.SetShaderParameter("giant", giant != null);
        if (giant != null)
        {
            _sky.SetShaderParameter("giant_color", C(giant.Color));
            _sky.SetShaderParameter("giant_band", C(giant.Band));
            _sky.SetShaderParameter("rings", giant.Rings);
            _sky.SetShaderParameter("ring_color", C(giant.RingColor));
        }
        Surfaces = new BodySurfaces(world.StarSystem, new D3(0, world.StarSystem.Home.Radius, 0));
        _sky.SetShaderParameter("surfaces", Surfaces.Texture);
        _sky.SetShaderParameter("home_ring", world.StarSystem.Home.Rings);
        _sky.SetShaderParameter("home_ring_color", C(Planets.Look.ArchetypeLook.For(world.Spec).RingColor));
        Sun.LightColor = C(s.StarColor);
    }

    /// <summary>The storm towers to draw: (x, z, radius, strength) in the frame, up to four.</summary>
    public void SetCells(Vector4[] cells, int count, float[] funnels = null)
    {
        _sky.SetShaderParameter("tower_wall", funnels ?? new float[4]);
        _sky.SetShaderParameter("tower", cells);
        _sky.SetShaderParameter("tower_count", count);
    }

    /// <summary>
    /// The front to draw (zeros for none): <paramref name="line"/> a point of its edge's straight line (x, z, the copy
    /// nearest the eye) and the way it advances; <paramref name="shape"/> its strength, period along the line, spacing
    /// of its copies and depth; <paramref name="wave"/> the next copy's shift along, its bows' depth, bows and bays;
    /// <paramref name="kind"/> a shelf cloud along it (1), a roll cloud ahead (1), its seed and severity.
    /// </summary>
    public void SetFront(Vector4 line, Vector4 shape, Vector4 wave, Vector4 kind)
    {
        _sky.SetShaderParameter("front_line", line);
        _sky.SetShaderParameter("front_shape", shape);
        _sky.SetShaderParameter("front_wave", wave);
        _sky.SetShaderParameter("front_kind", kind);
    }

    /// <summary>Growing cumulus to swell over fair-weather waterspouts: (x, z, radius, how far grown), up to two.</summary>
    public void SetCumulus(Vector4[] cells, int count)
    {
        _sky.SetShaderParameter("cumulus", cells);
        _sky.SetShaderParameter("cumulus_count", count);
    }

    /// <summary>Hands a rain shaft the sky's own cloud-base inputs, so its top meets the underside the sky draws.</summary>
    public void ShareCloudBase(ShaderMaterial m)
    {
        m.SetShaderParameter("cloud_noise", _noise);
        m.SetShaderParameter("cloud_scale", _sky.GetShaderParameter("cloud_scale"));
        m.SetShaderParameter("cloud_drift", _sky.GetShaderParameter("cloud_drift"));
        m.SetShaderParameter("cloud_base", _sky.GetShaderParameter("cloud_base"));
    }

    /// <summary>Screenshots: 1 shows cloud coverage, 2 cloud colour, 0 the normal sky.</summary>
    public void DebugView(int view) => _sky.SetShaderParameter("debug_view", view);

    /// <summary>Sets the time and the weather's air for this frame. <paramref name="latitude"/> is -1 (one pole) to 1 (the other): where you stand, unless <see cref="Stand"/> has said better.</summary>
    public void Advance(double delta, float latitude, AtmosphereState air, Vector2 wind)
    {
        if (System == null) return;
        if (!_stood) Stand(StarSystem.NoonLongitude, (latitude + 1f) * 0.5 * Math.PI, 0.0);
        Air = air;
        RunClock(delta);
        _cloudDrift += wind * (float)delta * 1.8f;
        if (wind.LengthSquared() > 0.01f) _sky.SetShaderParameter("storm_wind", wind.Normalized());
        Apply();
    }

    /// <summary>Runs the clock on: it never stops, whichever zone you are in.</summary>
    public void RunClock(double delta)
    {
        if (Paused || System == null) return;
        double t = TimeOfDay + delta * TimeScale / (System.DayMinutes * 60.0);
        double whole = Math.Floor(t);
        _wholeDays += whole;
        TimeOfDay = t - whole;
    }

    /// <summary>Where this sky is seen from: a point of the map at a height over it, so east, up and south are the ground's there.</summary>
    public void Stand(double longitude, double colatitude, double height)
    {
        if (Model == null) return;
        var (e, u, n) = StarSystem.FrameAt(longitude, colatitude);
        _skyToGlobe = new Basis(V(e), V(u), V(n));
        _eye = u * (Model.Home.Radius + height);
        _stood = true;
    }

    /// <summary>Where this sky is seen from, up in orbit: its directions are the zone's, and <paramref name="eye"/> is in the globe's frame.</summary>
    public void Stand(Basis worldToGlobe, D3 eye)
    {
        _skyToGlobe = worldToGlobe;
        _eye = eye;
        _stood = true;
    }

    static Vector3 V(D3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    Vector3 InSky(D3 globe) => (_skyToGlobe.Inverse() * V(globe)).Normalized();

    /// <summary>The direction to the sun at a time of day (0 midnight, 0.5 noon) from where you stand.</summary>
    public Vector3 SunAt(double timeOfDay) => InSky(Model.ToStar(_wholeDays + timeOfDay, _eye));

    /// <summary>Puts the sun, the moons, the giant and the stars where the system has them this moment, from where you stand.</summary>
    public void Track()
    {
        StarSystem m = Model;
        double days = Days;
        StarSystem.Orientation o = m.Orient(days);
        Surfaces?.Pump();
        (Vector3 Axis, Vector3 Zero) Turned(Orbiter b)
        {
            var (axis, zero) = m.Turned(b, days);
            return (InSky(o.ToGlobe(axis)), InSky(o.ToGlobe(zero)));
        }
        Basis system = new Basis(V(o.X), V(o.Y), V(o.Z)); // the globe's axes in the system's frame
        SkyToStars = system * _skyToGlobe;
        Vector3 toSun = InSky(m.See(m.Star, days, _eye, o).Direction);
        ToSun = toSun;
        StarInField = (V(m.Position(m.Star, days) - m.Position(m.Home, days))).Normalized();
        Sun.LookAt(Sun.GlobalPosition - toSun, MathF.Abs(toSun.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
        Skew();
        _sky.SetShaderParameter("star_turn", Stars.Field * SkyToStars);
        _sky.SetShaderParameter("sky_to_globe", _skyToGlobe);
        // The eye against the sphere the ring stands round, at the height it really is over it.
        double ringSphere = RingRadius > 0f ? RingRadius : m.Home.Radius;
        _sky.SetShaderParameter("home_ring_eye", V(_eye.Normalized * (1.0 + (_eye.Length - m.Home.Radius) / ringSphere)));

        _seen.Clear();
        foreach (Orbiter b in m.Bodies)
            if (b.Kind != OrbiterKind.Star && b.Kind != OrbiterKind.Belt && b != m.Home) _seen.Add(m.See(b, days, _eye, o));
        _seen.Sort((a, b) => b.Angular.CompareTo(a.Angular));
        _drawn.Clear();
        _brightest = Vector3.Up;
        _brightestEnergy = 0.06f; // starlight
        double giantAt = -1.0;
        _inSlots.Clear();
        foreach (Sighting s in _seen)
        {
            if (s.Body != m.Neighbour)
            {
                if (_inSlots.Count < BodySlots) _inSlots.Add(s);
                continue;
            }
            Vector3 dir = InSky(s.Direction), light = InSky(s.ToItsLight);
            giantAt = s.Distance;
            _sky.SetShaderParameter("giant_dir", dir);
            _sky.SetShaderParameter("giant_radius", s.Angular);
            _sky.SetShaderParameter("giant_light", light);
            var (giantAxis, giantZero) = Turned(s.Body);
            _sky.SetShaderParameter("giant_layer", Surfaces?.LayerOf(s.Body) ?? -1);
            _sky.SetShaderParameter("giant_zero", giantZero);
            NeighbourRing = giantAxis;
            _sky.SetShaderParameter("ring_normal", NeighbourRing);
            float lit = Math.Clamp(-dir.Dot(light) * 0.5f + 0.5f, 0f, 1f);
            if (dir.Y > 0f && 0.25f * lit > _brightestEnergy) (_brightestEnergy, _brightest) = (0.25f * lit, dir);
            _drawn.Add((s.Body, dir, s.Angular));
        }
        _inSlots.Sort((a, b) => b.Distance.CompareTo(a.Distance));
        int slot = 0, behindGiant = 0;
        foreach (Sighting s in _inSlots)
        {
            Vector3 dir = InSky(s.Direction), light = InSky(s.ToItsLight);
            if (s.Distance > giantAt) behindGiant++;
            _bodyDirs[slot] = dir;
            _bodyLights[slot] = light;
            _bodyRadii[slot] = s.Angular;
            (_bodyAxes[slot], _bodyZeros[slot]) = Turned(s.Body);
            _bodyLayers[slot] = Surfaces?.LayerOf(s.Body) ?? -1;
            _bodyColors[slot++] = new Vector3(s.Body.Color.R, s.Body.Color.G, s.Body.Color.B);
            float phase = Math.Clamp(-dir.Dot(light) * 0.5f + 0.5f, 0f, 1f); // full when opposite the sun
            float e = 0.22f * phase * Smooth(-0.02f, 0.2f, dir.Y) * (s.Angular / 0.03f);
            if (e > _brightestEnergy) (_brightestEnergy, _brightest) = (e, dir);
            _drawn.Add((s.Body, dir, s.Angular));
        }
        _sky.SetShaderParameter("giant_at", behindGiant);
        _sky.SetShaderParameter("moon_count", slot);
        _sky.SetShaderParameter("moon_dir", _bodyDirs);
        _sky.SetShaderParameter("moon_light", _bodyLights);
        _sky.SetShaderParameter("moon_radius", _bodyRadii);
        _sky.SetShaderParameter("moon_color", _bodyColors);
        _sky.SetShaderParameter("moon_layer", _bodyLayers);
        _sky.SetShaderParameter("moon_axis", _bodyAxes);
        _sky.SetShaderParameter("moon_zero", _bodyZeros);
    }

    /// <summary>Recomputes lights and the sky from the current time and air (screenshots call it directly).</summary>
    public void Apply()
    {
        SkySystem s = System;
        Track();
        Vector3 toSun = ToSun;
        float day = Smooth(-0.12f, 0.12f, toSun.Y);
        Daylight = day;
        AtmosphereState air = Air;

        // The sun: star-coloured, warming toward the horizon, dimmed by weather and, low down, by
        // the long path its light takes through the air (to under half near the horizon).
        float low = Smooth(0.35f, 0f, toSun.Y);
        Rgb light = Rgb.Lerp(s.StarColor, s.Sunset, low * 0.7f);
        Sun.LightColor = C(light);
        Sun.LightEnergy = s.SunEnergy * Smooth(-0.05f, 0.1f, toSun.Y) * AirMass(toSun.Y) * air.SunScale;
        Sun.ShadowOpacity = Math.Clamp(0.25f + air.SunScale, 0f, 1f);
        // The sun stays the sky's light even below the horizon (the shader reads its direction for
        // twilight); it just gives no light and casts no shadows then.
        Sun.ShadowEnabled = toSun.Y > -0.03f;

        // Night light: the brightest moon (or the giant's reflected light, or starlight), so night is dark but playable.
        // It takes over the sun's shadows once the sun has none, so it stops at walls and roofs too: one
        // shadowed light at a time, and it stays out until the sun has gone so the two never leak past each other.
        NightLight.ShadowEnabled = !Sun.ShadowEnabled;
        NightLight.LightEnergy = Smooth(-0.03f, -0.08f, toSun.Y) * Math.Clamp(_brightestEnergy, 0.06f, 0.35f) * air.SunScale;
        NightLight.LightColor = new Color(0.62f, 0.7f, 0.95f);
        NightLight.LookAt(NightLight.GlobalPosition - (_brightest.Y > 0.1f ? _brightest : new Vector3(0.3f, 1f, 0.2f).Normalized()), Vector3.Forward);
        Stars.Poll();
        GalaxySky.SetPixel(_sky, IsInsideTree() ? GetViewport() : null);

        // Clouds and overcast from the weather.
        _sky.SetShaderParameter("cloud_cover", air.CloudCover);
        _sky.SetShaderParameter("cloud_dark", air.CloudDarkness);
        _sky.SetShaderParameter("cloud_base", air.CloudBase);
        _sky.SetShaderParameter("cloud_thick", air.CloudThickness);
        _sky.SetShaderParameter("cloud_drift", _cloudDrift);
        _sky.SetShaderParameter("overcast", air.Overcast);
        _sky.SetShaderParameter("stratus", air.Stratus);
        _sky.SetShaderParameter("cirrus", air.Cirrus);
        Rgb grey = Rgb.Lerp(Rgb.Lerp(s.Horizon, new Rgb(0.62f, 0.64f, 0.66f), 0.7f), new Rgb(0.2f, 0.21f, 0.24f), air.CloudDarkness);
        _sky.SetShaderParameter("overcast_color", C(grey));
        _sky.SetShaderParameter("cloud_lit", C(Rgb.Lerp(new Rgb(1f, 1f, 1f), s.StarColor, 0.12f) * s.CloudTint));
        // Ash over lava glows red from underneath, more so as daylight fades.
        _sky.SetShaderParameter("cloud_underglow", s.Air == AirKind.Ash ? new Color(0.55f, 0.16f, 0.05f) * (0.35f + 0.65f * (1f - day)) : new Color(0, 0, 0));
        _sky.SetShaderParameter("cloud_shade", C(Rgb.Lerp(Rgb.Lerp(s.Zenith, new Rgb(0.55f, 0.58f, 0.62f), 0.6f) * s.CloudTint, new Rgb(0.16f, 0.17f, 0.2f), air.CloudDarkness)));

        // Fog: the air's haze colour, lit by the sun, greyed by overcast, dark at night. It is lit
        // as the ground is: by a sun that has climbed clear of the horizon's air, so haze and
        // valley fog under a low sun are dim and warm, never a white wall. The glow toward the sun
        // (sun scatter) is kept modest: a low sun shines through the haze, it doesn't blind.
        float fogLight = Smooth(-0.16f, 0.25f, toSun.Y);
        Rgb fog = Rgb.Lerp(s.Haze, grey, air.Overcast * 0.8f) * (0.08f + 0.92f * fogLight);
        fog = Rgb.Lerp(fog, s.Sunset * (0.8f * fogLight), low * day * 0.35f);
        Environment.FogLightColor = C(fog);
        Environment.FogDensity = air.FogDensity;
        Environment.FogSkyAffect = air.FogSkyAffect;
        // The scatter toward the sun and the glitter off water are the sun's own light: a low sun has
        // little left to give them, or the haze and the sea go white under it.
        float climbed = Smooth(0.02f, 0.38f, toSun.Y);
        Environment.FogSunScatter = (0.08f + 0.12f * s.Density) * (1f - air.Overcast) * (0.25f + 0.75f * climbed);
        Sun.LightSpecular = 0.2f + 0.8f * climbed;
        // Ash worlds are lit as much by their lava as their sun: never quite black.
        float floor = s.Air == AirKind.Ash ? 0.45f : 0.25f;
        Environment.AmbientLightEnergy = (floor + (1f - floor) * day) * air.AmbientScale;
        Environment.AdjustmentSaturation = air.Saturation;
    }

    /// <summary>
    /// Pools valley fog below a height: the low ground around the player plus the fog's depth. Godot's
    /// height fog thickens below that plane, so valleys fill and ridges stand clear.
    ///
    /// Morning fog forms overnight and burns off as the sun climbs: after noon only the weather's own
    /// ground fog lies in the valleys, so a setting sun brings none. And height fog thickens with how
    /// deep a point lies, not how far it is: seen from inside the bank, the ground at your feet would
    /// be as white as the far side of the valley. So once the camera is down in it, the layer gives
    /// way to plain distance fog: you see through it near you, and it closes in with distance.
    /// </summary>
    public void SetValleyFog(float floor, AtmosphereState air)
    {
        float valley = TimeOfDay < 0.5 ? air.ValleyFog : MathF.Min(air.ValleyFog, air.GroundFog);
        float depth = Atmosphere.ValleyFogDepthOf(valley);
        float top = floor + depth;
        Camera3D camera = IsInsideTree() ? GetViewport()?.GetCamera3D() : null;
        float inside = camera == null ? 0f : Math.Clamp((top - camera.GlobalPosition.Y) / depth, 0f, 1f);
        float layer = 1f - Smooth(0f, 0.3f, inside);
        Environment.FogHeight = top;
        Environment.FogHeightDensity = valley * 0.09f * layer;
        // The haze lies low: from high up the far land is seen through less of it (Atmosphere.FogFrom).
        float over = camera == null ? 0f : camera.GlobalPosition.Y - floor;
        Environment.FogDensity = Atmosphere.FogFrom(air.FogDensity, over) + valley * 0.004f * (1f - layer);
    }

    /// <summary>
    /// How much of the sun's light gets through the air to the ground, by its height (the y of the
    /// direction to it): all of it from about 17° up, under half at the horizon.
    /// </summary>
    public static float AirMass(float sunY) => 0.45f + 0.55f * Smooth(0.02f, 0.3f, sunY);

    static Color C(Rgb c) => new(c.R, c.G, c.B);

    /// <summary>The catalogue's name for the world's own star class.</summary>
    public static StarKind KindOf(StarClass c) => c switch
    {
        StarClass.RedDwarf => StarKind.RedDwarf,
        StarClass.Orange => StarKind.OrangeDwarf,
        StarClass.Yellow => StarKind.YellowDwarf,
        StarClass.White => StarKind.WhiteStar,
        _ => StarKind.BlueGiant,
    };

    static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
